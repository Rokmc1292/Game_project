using System;
using System.Collections;
using LiarsBatting.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LiarsBatting.Network
{
    public enum MatchSide { A, B }

    // Drives one online match over a single Firestore document (matches/{id}).
    // No Cloud Functions: every write is done by whichever side the turn
    // protocol says should act next, so there's never a race between the two
    // clients writing at once. A monotonic `seq` field is how each client tells
    // "opponent did something new" apart from "I'm seeing my own write echoed
    // back" -- every Submit* call bumps its LOCAL seq the moment it writes, so
    // the poll loop only ever reacts to a seq higher than what it already knows.
    //
    // Nobody's raw secret is ever written to Firestore. A defender keeps the
    // true result of the attacker's guess in local memory only (_myTrueResult)
    // and uses it later purely to judge a challenge -- the shared document only
    // ever carries the (possibly false) REPORTED result and single revealed
    // digits, exactly what the local AI-mode UI already limits the player to.
    public class NetworkMatchController
    {
        private const float PollIntervalSeconds = 0.5f;

        private readonly FirestoreClient _db;
        private readonly MonoBehaviour _host;
        private readonly string _matchId;
        private readonly string _myNickname;

        public readonly MatchSide MySide;
        public readonly string OpponentNickname;
        public MatchSide CurrentAttacker => _turnAttacker;

        private Coroutine _pollRoutine;
        private long _lastSeq;

        // ---- presence (disconnect detection) ----
        // Each side writes a counter to its OWN small document (matchPresence/{matchId}_{A|B}) every
        // few seconds and reads the other side's. A counter that stops changing for long enough
        // means the opponent closed the game or lost its connection. Separate documents (not the
        // match doc) because the match doc is fully replaced on every turn write. Staleness is
        // only judged while my own reads are succeeding, so my own outage isn't blamed on them.
        private const float HeartbeatSeconds = 3f;
        private const float DisconnectAfterSeconds = 30f;
        private Coroutine _presenceRoutine;
        private long _myBeat;
        private long _lastOpponentBeat = -1;
        private float _lastOpponentBeatAt;
        private float _lastPresenceReadAt;
        private bool _disconnectReported;

        // The opponent stopped sending presence for DisconnectAfterSeconds.
        public event Action OnOpponentDisconnected;
        public const string GameOverByDisconnect = "disconnect";

        // Local mirror of the persistent (non-transient) parts of the document,
        // since every write replaces the whole thing.
        private string _heroA = "";
        private string _heroB = "";
        private bool _heroSelectCompleted;
        private bool _secretASet;
        private bool _secretBSet;
        private bool _setupCompleted;
        private MatchSide _turnAttacker;
        private string _status = "waiting_secrets";
        private string _winner = "";

        // Warrior's "keep the turn" flag: set by the attacker when submitting a
        // guess, and must survive every write in between (guess -> response ->
        // maybe challenge/reveal) since SetDocument replaces the whole document.
        // Whichever side ends up writing the turn-ending action (could be either
        // side, depending on who was caught) checks this before flipping.
        private bool _warriorKeepTurn;
        public bool KeepsCurrentTurn => _warriorKeepTurn;

        private string PlayerA => MySide == MatchSide.A ? _myNickname : OpponentNickname;
        private string PlayerB => MySide == MatchSide.A ? OpponentNickname : _myNickname;

        private JudgeResult? _myTrueResultForCurrentDefense;
        private JudgeResult _myLastReportedResult;

        public event Action<HeroId> OnBothHeroesReady;
        public event Action OnBothSecretsReady;
        public event Action<int[]> OnOpponentGuess;
        public event Action<JudgeResult> OnOpponentResponse;
        public event Action OnChallengeNeedsMyJudgement;
        public event Action OnIMustReveal;
        // A digit the opponent was forced to open by a trust/challenge outcome.
        public event Action<MatchSide, int, int> OnOpponentRevealed;
        // The opponent (as attacker) trusted my answer: no challenge, no reveal, the turn just moves on.
        public event Action OnOpponentTrusted;
        public event Action<MatchSide> OnTurnChanged;
        // (winner, reason). Fires on BOTH sides: for the side that writes the "finished" document
        // it fires locally, since my own write's echo is filtered out of my own poll (the seq gate)
        // and the loser is exactly the side that writes it.
        public event Action<MatchSide, string> OnGameOver;
        // Fires on the side that did NOT surrender (the surrenderer ends locally).
        public event Action OnOpponentSurrendered;

        public const string GameOverByGuess = "guess";
        public const string GameOverByFullReveal = "fullReveal";
        // The first mover's guess was right but the other side's last guess missed (see MatchRules).
        public const string GameOverByLastGuessMissed = "lastGuessMissed";
        // Both sides found each other's secret in the same round: nobody wins.
        public const string GameOverByDraw = "draw";

        // Player A always moves first; the first mover's correct guess gives the other side one last guess.
        public const MatchSide FirstMover = MatchSide.A;

        // ---- last guess (the draw rule) ----
        // Attacker's client: the defender confirmed my guess was exactly right. Because I moved first,
        // it is up to ME (only I know whether my Warrior still holds its ability) whether that wins
        // outright or hands the defender a last guess.
        public event Action OnMyGuessSolved;
        // Defender's client: the first mover solved my secret and gave me one last guess.
        public event Action OnLastGuessGranted;
        // First mover's client: the defender's last guess arrived, to be judged against my secret.
        public event Action<int[]> OnLastGuessReceived;

        // ---- rematch vote (after a draw) ----
        // The opponent asked for a rematch / said no / the vote timed out (bool = nobody answered).
        public event Action OnRematchRequestedByOpponent;
        public event Action<bool> OnRematchDeclined;
        // Both said yes but the next match could not be created.
        public event Action<string> OnRematchError;
        // Both said yes: the id of the fresh match document to play in.
        public event Action<string> OnRematchReady;

        public MatchSide OpponentSide => OtherSide(MySide);
        public event Action<string> OnError;

        // Rogue: fires on the ATTACKER's client once the defender truthfully
        // answers a "was that a lie?" query.
        public event Action<bool> OnRogueAnswer;
        // Priest: fires on the DEFENDER's client when queried about a specific
        // (position, guessedDigit) pair, and on the ATTACKER's client once the
        // yes/no answer comes back.
        public event Action<int, int> OnOpponentPriestQuery;
        public event Action<bool> OnPriestAnswer;
        // Wizard: fires on the RESPONDING side (whoever didn't use the ability)
        // to tell them to reveal one of their own random digits in reply.
        public event Action OnMustRevealForWizard;
        // A digit the opponent opened through a Wizard ability (their own pick, or their reply to mine).
        public event Action<int, int> OnOpponentWizardReveal;

        public NetworkMatchController(FirestoreClient db, MonoBehaviour host, string matchId,
            MatchSide mySide, string myNickname, string opponentNickname)
        {
            _db = db;
            _host = host;
            _matchId = matchId;
            MySide = mySide;
            _myNickname = myNickname;
            OpponentNickname = opponentNickname;
        }

        private string DocPath => $"matches/{_matchId}";
        private static string SideKey(MatchSide s) => s == MatchSide.A ? "A" : "B";
        private MatchSide OtherSide(MatchSide s) => s == MatchSide.A ? MatchSide.B : MatchSide.A;

        public void Start()
        {
            _pollRoutine = _host.StartCoroutine(PollLoop());
            _lastOpponentBeatAt = Time.realtimeSinceStartup;   // grace period while they load in
            _lastPresenceReadAt = Time.realtimeSinceStartup;
            _presenceRoutine = _host.StartCoroutine(PresenceLoop());
        }

        public void Stop()
        {
            StopRematchVote();
            if (_pollRoutine != null)
            {
                _host.StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }
            if (_presenceRoutine != null)
            {
                _host.StopCoroutine(_presenceRoutine);
                _presenceRoutine = null;
                _db.DeleteDocument(PresencePath(MySide), null, null);   // tidy up; nothing depends on it
            }
        }

        private string PresencePath(MatchSide side) => $"matchPresence/{_matchId}_{SideKey(side)}";

        private IEnumerator PresenceLoop()
        {
            while (true)
            {
                _myBeat++;
                _db.SetDocument(PresencePath(MySide),
                    new JObject { ["beat"] = FirestoreClient.IntField(_myBeat) }, null, null);

                _db.GetDocument(PresencePath(OtherSide(MySide)), doc =>
                {
                    float now = Time.realtimeSinceStartup;
                    _lastPresenceReadAt = now;
                    long beat = FirestoreClient.ReadInt(doc, "beat", -1);
                    if (doc != null && beat != _lastOpponentBeat)
                    {
                        _lastOpponentBeat = beat;
                        _lastOpponentBeatAt = now;
                    }
                }, _ => { });

                float t = Time.realtimeSinceStartup;
                bool myReadsWork = t - _lastPresenceReadAt < HeartbeatSeconds * 3;
                if (!_disconnectReported && myReadsWork && t - _lastOpponentBeatAt > DisconnectAfterSeconds)
                {
                    _disconnectReported = true;
                    OnOpponentDisconnected?.Invoke();
                }

                yield return new WaitForSecondsRealtime(HeartbeatSeconds);
            }
        }

        private IEnumerator PollLoop()
        {
            while (true)
            {
                PollOnce();
                yield return new WaitForSeconds(PollIntervalSeconds);
            }
        }

        private void PollOnce()
        {
            _db.GetDocument(DocPath, doc =>
            {
                if (doc == null) return;

                _heroA = FirestoreClient.ReadString(doc, "heroA", "");
                _heroB = FirestoreClient.ReadString(doc, "heroB", "");
                _secretASet = FirestoreClient.ReadBool(doc, "secretASet");
                _secretBSet = FirestoreClient.ReadBool(doc, "secretBSet");
                // Checked on every poll, NOT gated by seq: MarkMyHeroReady/
                // MarkMySecretReady write their own readiness as a field-masked
                // patch (so the two setup writes can't stomp on each other) and
                // that patch never touches `seq`, so the seq-gate below would
                // never notice it. Heroes must both be in before secrets, since
                // DemonHunter's restriction has to be known before the OTHER
                // side picks their secret.
                if (!_heroSelectCompleted && _heroA != "" && _heroB != "")
                {
                    _heroSelectCompleted = true;
                    string opponentHeroStr = MySide == MatchSide.A ? _heroB : _heroA;
                    OnBothHeroesReady?.Invoke((HeroId)Enum.Parse(typeof(HeroId), opponentHeroStr));
                    return;
                }

                if (!_setupCompleted && _secretASet && _secretBSet)
                {
                    _setupCompleted = true;
                    OnBothSecretsReady?.Invoke();
                    return;
                }

                long seq = FirestoreClient.ReadInt(doc, "seq", 0);
                if (seq <= _lastSeq) return; // nothing new, or it's my own write echoing back
                _lastSeq = seq;

                // Only a NEW document may change who has the turn. Read before the check above, a response
                // that was already in flight when I wrote (carrying the turn from before my write) could
                // put the old turn owner back and let both sides attack at once.
                _status = FirestoreClient.ReadString(doc, "status", _status);
                _turnAttacker = FirestoreClient.ReadString(doc, "turnAttacker", "A") == "A" ? MatchSide.A : MatchSide.B;
                _warriorKeepTurn = FirestoreClient.ReadBool(doc, "warriorKeepTurn", false);

                string action = FirestoreClient.ReadString(doc, "action", "");
                switch (action)
                {
                    case "guess":
                        OnOpponentGuess?.Invoke(DecodeGuess(FirestoreClient.ReadString(doc, "guessDigits", "")));
                        break;

                    case "response":
                        var reported = DecodeResult(doc, "reported");
                        OnOpponentResponse?.Invoke(reported);
                        break;

                    case "challenge":
                        OnChallengeNeedsMyJudgement?.Invoke();
                        break;

                    case "solved":
                        OnMyGuessSolved?.Invoke();
                        break;

                    case "last_guess_granted":
                        OnLastGuessGranted?.Invoke();
                        break;

                    case "last_guess":
                        OnLastGuessReceived?.Invoke(DecodeGuess(FirestoreClient.ReadString(doc, "guessDigits", "")));
                        break;

                    case "reveal_needed":
                        string target = FirestoreClient.ReadString(doc, "revealTarget", "");
                        if (target == SideKey(MySide)) OnIMustReveal?.Invoke();
                        break;

                    case "turn_done":
                        // Three things end a turn: a reveal (after a challenge), a Priest answer, or
                        // -- with neither -- the attacker simply trusting my answer.
                        string revealedBy = FirestoreClient.ReadString(doc, "revealedBy", "");
                        bool hadReveal = !string.IsNullOrEmpty(revealedBy);
                        bool isPriestAnswer = FirestoreClient.HasField(doc, "priestAnswerBool");
                        if (hadReveal && revealedBy != SideKey(MySide))
                        {
                            var side = revealedBy == "A" ? MatchSide.A : MatchSide.B;
                            int idx = (int)FirestoreClient.ReadInt(doc, "revealedIndex", -1);
                            int digit = (int)FirestoreClient.ReadInt(doc, "revealedDigit", -1);
                            OnOpponentRevealed?.Invoke(side, idx, digit);
                        }
                        if (isPriestAnswer)
                            OnPriestAnswer?.Invoke(FirestoreClient.ReadBool(doc, "priestAnswerBool", false));
                        else if (!hadReveal)
                            OnOpponentTrusted?.Invoke();
                        OnTurnChanged?.Invoke(_turnAttacker);
                        break;

                    case "finished":
                        // A last digit that ended the match travels inside the "finished" write itself.
                        string finalRevealer = FirestoreClient.ReadString(doc, "revealedBy", "");
                        if (finalRevealer != "" && finalRevealer != SideKey(MySide))
                            OnOpponentRevealed?.Invoke(finalRevealer == "A" ? MatchSide.A : MatchSide.B,
                                (int)FirestoreClient.ReadInt(doc, "revealedIndex", -1),
                                (int)FirestoreClient.ReadInt(doc, "revealedDigit", -1));
                        string finalResponder = FirestoreClient.ReadString(doc, "wizardResponderSide", "");
                        if (finalResponder != "" && finalResponder != SideKey(MySide))
                            OnOpponentWizardReveal?.Invoke(
                                (int)FirestoreClient.ReadInt(doc, "wizardResponderIndex", -1),
                                (int)FirestoreClient.ReadInt(doc, "wizardResponderDigit", -1));

                        if (FirestoreClient.ReadBool(doc, "surrendered", false))
                        {
                            OnOpponentSurrendered?.Invoke();
                            break;
                        }
                        string winner = FirestoreClient.ReadString(doc, "winner", "");
                        string reason = FirestoreClient.ReadString(doc, "reason", GameOverByGuess);
                        OnGameOver?.Invoke(winner == "A" ? MatchSide.A : MatchSide.B, reason);
                        break;

                    case "rogue_query":
                        // Only the defender (who alone has the true result on hand)
                        // can answer -- if I'm not currently defending, this isn't
                        // addressed to me at this moment.
                        if (_myTrueResultForCurrentDefense.HasValue)
                            WriteRogueAnswer(DidIActuallyLie());
                        break;

                    case "rogue_answer":
                        bool wasLie = FirestoreClient.ReadBool(doc, "rogueWasLie", false);
                        OnRogueAnswer?.Invoke(wasLie);
                        break;

                    case "priest_query":
                        long queryPos = FirestoreClient.ReadInt(doc, "priestPosition", -1);
                        long queryDigit = FirestoreClient.ReadInt(doc, "priestGuessDigit", -1);
                        if (queryPos >= 0) OnOpponentPriestQuery?.Invoke((int)queryPos, (int)queryDigit);
                        break;

                    case "wizard_use":
                        string userSide = FirestoreClient.ReadString(doc, "wizardUserSide", "");
                        if (userSide != SideKey(MySide))
                        {
                            int idx = (int)FirestoreClient.ReadInt(doc, "wizardUserIndex", -1);
                            int digit = (int)FirestoreClient.ReadInt(doc, "wizardUserDigit", -1);
                            OnOpponentWizardReveal?.Invoke(idx, digit);
                            OnMustRevealForWizard?.Invoke();
                        }
                        break;

                    case "wizard_response":
                        string responderSide = FirestoreClient.ReadString(doc, "wizardResponderSide", "");
                        if (responderSide != SideKey(MySide))
                        {
                            int idx = (int)FirestoreClient.ReadInt(doc, "wizardResponderIndex", -1);
                            int digit = (int)FirestoreClient.ReadInt(doc, "wizardResponderDigit", -1);
                            OnOpponentWizardReveal?.Invoke(idx, digit);
                        }
                        break;
                }
            }, error => OnError?.Invoke(error));
        }

        // ---------- writes (only ever called when it's legitimately my turn to act) ----------

        // Same field-masked-patch approach as MarkMySecretReady, and for the same
        // reason: both players pick a hero at roughly the same time, with no
        // "whose turn" to keep the two writes apart.
        public void MarkMyHeroReady(HeroId hero)
        {
            string myField = MySide == MatchSide.A ? "heroA" : "heroB";
            var fields = new JObject { [myField] = FirestoreClient.StringField(hero.ToString()) };
            _db.PatchFields(DocPath, fields, new[] { myField }, () =>
            {
                if (MySide == MatchSide.A) _heroA = hero.ToString(); else _heroB = hero.ToString();
                PollOnce();
            }, error => OnError?.Invoke(error));
        }

        // Unlike every later turn-based Submit*, both players can call this at
        // roughly the same time -- there's no "whose turn" to keep them apart.
        // A field-masked patch (touching ONLY my own flag) means the two writes
        // genuinely can't stomp on each other regardless of timing, unlike a
        // normal WriteBaseDoc full-document replace -- a read-then-write here
        // still has a race window if both players click "게임 시작" together.
        public void MarkMySecretReady()
        {
            string myField = MySide == MatchSide.A ? "secretASet" : "secretBSet";
            var fields = new JObject { [myField] = FirestoreClient.BoolField(true) };
            _db.PatchFields(DocPath, fields, new[] { myField }, () =>
            {
                if (MySide == MatchSide.A) _secretASet = true; else _secretBSet = true;
                PollOnce(); // check right away whether the opponent already is too
            }, error => OnError?.Invoke(error));
        }

        // keepTurn is Warrior's ability: if true, whichever write eventually ends
        // this turn (SubmitTrust or either side's SubmitReveal) will NOT pass the
        // turn to the other side.
        public void SubmitGuess(int[] guess, bool keepTurn = false)
        {
            _warriorKeepTurn = keepTurn; // persisted on every write from here by WriteBaseDoc
            var extra = new JObject { ["guessDigits"] = FirestoreClient.StringField(EncodeGuess(guess)) };
            WriteBaseDoc("guess", extra);
        }

        // trueResult never leaves this device; only `reported` (possibly a lie) is written.
        // Checked on `reported`, not `trueResult`: a Hunter defender is allowed to
        // report a fake non-win even when trueResult really is a win, and that
        // must continue the match normally rather than force game over here.
        public void SubmitDefenseResponse(JudgeResult trueResult, JudgeResult reported)
        {
            if (reported.IsWin)
            {
                _myTrueResultForCurrentDefense = null;
                if (OpponentSide == FirstMover)
                {
                    // The first mover guessed my secret: they decide (Warrior keeping its ability wins
                    // outright) whether I get one last guess -- see OnMyGuessSolved.
                    WriteBaseDoc("solved", null);
                    return;
                }
                SubmitGameOver(OtherSide(MySide)); // the second mover guessed my secret exactly: that wins
                return;
            }

            _myTrueResultForCurrentDefense = trueResult;
            _myLastReportedResult = reported;
            var extra = new JObject
            {
                ["reportedStrike"] = FirestoreClient.IntField(reported.Strike),
                ["reportedBall"] = FirestoreClient.IntField(reported.Ball)
            };
            WriteBaseDoc("response", extra);
        }

        // Call from the OnChallengeNeedsMyJudgement handler to find out, using
        // only locally-held information, whether the attacker's challenge is
        // actually correct -- then pass the result straight to ResolveChallenge.
        public bool DidIActuallyLie()
            => _myTrueResultForCurrentDefense.HasValue &&
               !_myTrueResultForCurrentDefense.Value.Equals(_myLastReportedResult);

        public void SubmitTrust()
        {
            if (!_warriorKeepTurn) _turnAttacker = OtherSide(_turnAttacker);
            _warriorKeepTurn = false; // consumed regardless of who ends up owning the next turn
            WriteBaseDoc("turn_done", null);
        }

        public void SubmitChallenge()
        {
            WriteBaseDoc("challenge", null);
        }

        // Called by the DEFENDER after comparing the attacker's challenge against
        // the true result only they know. attackerWasRight == true means the
        // defender lied and got caught; false means the attacker guessed wrong.
        public void ResolveChallenge(bool attackerWasRight)
        {
            var revealTarget = attackerWasRight ? MySide : OtherSide(MySide);
            var extra = new JObject { ["revealTarget"] = FirestoreClient.StringField(SideKey(revealTarget)) };
            WriteBaseDoc("reveal_needed", extra, () =>
            {
                if (revealTarget == MySide) OnIMustReveal?.Invoke(); // no need to wait for my own poll
            });
        }

        public void SubmitReveal(int index, int digit)
        {
            if (!_warriorKeepTurn) _turnAttacker = OtherSide(_turnAttacker);
            _warriorKeepTurn = false;
            var extra = new JObject
            {
                ["revealedBy"] = FirestoreClient.StringField(SideKey(MySide)),
                ["revealedIndex"] = FirestoreClient.IntField(index),
                ["revealedDigit"] = FirestoreClient.IntField(digit)
            };
            WriteBaseDoc("turn_done", extra);
        }

        // reason lets the other client say WHY the match ended (guessed secret vs. every digit opened).
        public void SubmitGameOver(MatchSide winner, string reason = GameOverByGuess)
        {
            _winner = SideKey(winner);
            var extra = new JObject
            {
                ["winner"] = FirestoreClient.StringField(_winner),
                ["reason"] = FirestoreClient.StringField(reason)
            };
            WriteBaseDoc("finished", extra);
            // The side that writes this is the loser, and its own write is filtered out of its own
            // poll -- without this it would never be told the match is over (no defeat screen).
            OnGameOver?.Invoke(winner, reason);
        }

        // The reveal that opens my LAST hidden digit ends the match, so the two facts go out as ONE write.
        // (Two back-to-back writes replace each other: the opponent would poll only the "finished" one and
        // never see the final digit, and a reordered pair could even erase the ending.) The other side
        // applies the digit first, then the ending -- see the "finished" case in PollOnce.
        public void SubmitRevealAndGameOver(int index, int digit, MatchSide winner, string reason)
        {
            _winner = SideKey(winner);
            var extra = new JObject
            {
                ["revealedBy"] = FirestoreClient.StringField(SideKey(MySide)),
                ["revealedIndex"] = FirestoreClient.IntField(index),
                ["revealedDigit"] = FirestoreClient.IntField(digit),
                ["winner"] = FirestoreClient.StringField(_winner),
                ["reason"] = FirestoreClient.StringField(reason)
            };
            WriteBaseDoc("finished", extra);
            OnGameOver?.Invoke(winner, reason);
        }

        // Same, for the reply to the opponent's Clairvoyance that opens my last hidden digit.
        public void SubmitWizardResponseAndGameOver(int index, int digit, MatchSide winner, string reason)
        {
            _winner = SideKey(winner);
            var extra = new JObject
            {
                ["wizardResponderSide"] = FirestoreClient.StringField(SideKey(MySide)),
                ["wizardResponderIndex"] = FirestoreClient.IntField(index),
                ["wizardResponderDigit"] = FirestoreClient.IntField(digit),
                ["winner"] = FirestoreClient.StringField(_winner),
                ["reason"] = FirestoreClient.StringField(reason)
            };
            WriteBaseDoc("finished", extra);
            OnGameOver?.Invoke(winner, reason);
        }

        // Ends the match immediately with the other side as the winner. Written
        // as a normal "finished" action plus a flag, so the opponent can tell it
        // apart from a guessed-secret win.
        public void SubmitSurrender()
        {
            _winner = SideKey(OtherSide(MySide));
            var extra = new JObject
            {
                ["winner"] = FirestoreClient.StringField(_winner),
                ["surrendered"] = FirestoreClient.BoolField(true)
            };
            WriteBaseDoc("finished", extra);
        }

        // ---------- last guess (the draw rule) ----------

        // First mover's client, after OnMyGuessSolved: the defender gets one last guess. Hitting it is a
        // draw, missing it a win for me. The turn moves to the defender for that one guess.
        public void SubmitGrantLastGuess()
        {
            _turnAttacker = OtherSide(MySide);
            _warriorKeepTurn = false;
            WriteBaseDoc("last_guess_granted", null);
        }

        // Defender's client: my one last guess at the first mover's secret.
        public void SubmitLastGuess(int[] guess)
        {
            var extra = new JObject { ["guessDigits"] = FirestoreClient.StringField(EncodeGuess(guess)) };
            WriteBaseDoc("last_guess", extra);
        }

        // First mover's client, after judging the last guess: it was right, so nobody wins.
        public void SubmitDraw()
        {
            _winner = "draw";
            var extra = new JObject
            {
                ["winner"] = FirestoreClient.StringField(_winner),
                ["reason"] = FirestoreClient.StringField(GameOverByDraw)
            };
            WriteBaseDoc("finished", extra);
            OnGameOver?.Invoke(MySide, GameOverByDraw);   // my own write never comes back through my own poll
        }

        // ---------- rematch vote (after a draw) ----------
        // Each side writes its answer ("yes" / "no") into its own field of the finished match document
        // (field-masked patches, so the two answers can't overwrite each other). Once both said yes the
        // first mover creates the next match document -- with the sides swapped, so whoever moved second
        // now moves first -- and publishes its id in `nextMatchId` for the other side to pick up.

        private const float RematchPollSeconds = 1f;
        private const float RematchTimeoutSeconds = 45f;   // how long a "yes" waits for an answer

        private Coroutine _rematchRoutine;
        private string _myRematchVote = "";
        private bool _opponentRematchAnnounced;
        private bool _rematchEnded;
        private bool _rematchCreating;
        private float _rematchRequestedAt;

        private string RematchField(MatchSide side) => side == MatchSide.A ? "rematchA" : "rematchB";

        // Starts listening for the opponent's answer. Call once the match is over (and Stop() was called).
        public void BeginRematchVote()
        {
            StopRematchVote();
            _myRematchVote = "";
            _opponentRematchAnnounced = false;
            _rematchEnded = false;
            _rematchCreating = false;
            _rematchRoutine = _host.StartCoroutine(RematchLoop());
        }

        public void StopRematchVote()
        {
            if (_rematchRoutine != null)
            {
                _host.StopCoroutine(_rematchRoutine);
                _rematchRoutine = null;
            }
        }

        public void RequestRematch(Action<string> onError)
        {
            if (_rematchRoutine == null || _rematchEnded) return;
            _myRematchVote = "yes";
            _rematchRequestedAt = Time.realtimeSinceStartup;
            WriteRematchVote("yes", error =>
            {
                _myRematchVote = "";   // not recorded: let the player try again
                onError?.Invoke(error);
            });
        }

        // Backing out (leaving the result popup) counts as a "no".
        public void DeclineRematch()
        {
            if (_rematchRoutine == null || _rematchEnded || _myRematchVote == "no") return;
            _myRematchVote = "no";
            WriteRematchVote("no", null);
        }

        private void WriteRematchVote(string vote, Action<string> onError)
        {
            string field = RematchField(MySide);
            var fields = new JObject { [field] = FirestoreClient.StringField(vote) };
            _db.PatchFields(DocPath, fields, new[] { field }, null, error => onError?.Invoke(error));
        }

        private IEnumerator RematchLoop()
        {
            while (true)
            {
                PollRematchOnce();
                yield return new WaitForSecondsRealtime(RematchPollSeconds);
            }
        }

        private void PollRematchOnce()
        {
            _db.GetDocument(DocPath, doc =>
            {
                if (doc == null || _rematchEnded || _myRematchVote == "no") return;

                string theirs = FirestoreClient.ReadString(doc, RematchField(OpponentSide), "");
                string next = FirestoreClient.ReadString(doc, "nextMatchId", "");

                if (theirs == "no")
                {
                    _rematchEnded = true;
                    OnRematchDeclined?.Invoke(false);
                    return;
                }
                if (theirs == "yes" && !_opponentRematchAnnounced)
                {
                    _opponentRematchAnnounced = true;
                    OnRematchRequestedByOpponent?.Invoke();
                }
                if (!string.IsNullOrEmpty(next) && _myRematchVote == "yes")
                {
                    _rematchEnded = true;
                    OnRematchReady?.Invoke(next);
                    return;
                }
                if (theirs == "yes" && _myRematchVote == "yes" && MySide == FirstMover && !_rematchCreating)
                {
                    CreateRematchMatch();
                    return;
                }
                if (_myRematchVote == "yes" && theirs == "" &&
                    Time.realtimeSinceStartup - _rematchRequestedAt > RematchTimeoutSeconds)
                {
                    _rematchEnded = true;
                    OnRematchDeclined?.Invoke(true);
                }
            }, _ => { });
        }

        private void CreateRematchMatch()
        {
            _rematchCreating = true;
            string newId = Guid.NewGuid().ToString("N");
            var matchFields = new JObject
            {
                ["playerA"] = FirestoreClient.StringField(PlayerB),   // sides swap: the old second mover opens
                ["playerB"] = FirestoreClient.StringField(PlayerA),
                ["status"] = FirestoreClient.StringField("matched")
            };
            _db.SetDocument($"matches/{newId}", matchFields, () =>
            {
                var pointer = new JObject { ["nextMatchId"] = FirestoreClient.StringField(newId) };
                _db.PatchFields(DocPath, pointer, new[] { "nextMatchId" }, () =>
                {
                    if (_rematchEnded) return;
                    _rematchEnded = true;
                    OnRematchReady?.Invoke(newId);
                }, FailRematch);
            }, FailRematch);
        }

        // Creating the next match failed (e.g. firestore.rules without the rematch fields): end the vote
        // instead of retrying every second and leaving a trail of orphan match documents.
        private void FailRematch(string error)
        {
            _rematchEnded = true;
            _rematchCreating = false;
            OnRematchError?.Invoke(error);
        }

        // ---------- Rogue ----------

        // Called by the ATTACKER, after receiving a response, to ask the
        // defender for a guaranteed-truthful yes/no on whether they lied.
        public void SubmitRogueQuery() => WriteBaseDoc("rogue_query", null);

        private void WriteRogueAnswer(bool wasLie)
        {
            var extra = new JObject { ["rogueWasLie"] = FirestoreClient.BoolField(wasLie) };
            WriteBaseDoc("rogue_answer", extra);
        }

        // ---------- Priest ----------

        // Called by the ATTACKER instead of SubmitGuess -- asks "is position X
        // digit Y?" instead of a full 4-digit guess. Still costs the whole turn.
        public void SubmitPriestQuery(int position, int guessedDigit)
        {
            var extra = new JObject
            {
                ["priestPosition"] = FirestoreClient.IntField(position),
                ["priestGuessDigit"] = FirestoreClient.IntField(guessedDigit)
            };
            WriteBaseDoc("priest_query", extra);
        }

        // Called by the DEFENDER with their (possibly false) yes/no answer. Ends
        // the turn exactly like SubmitTrust would -- there's no challenge step
        // for a Priest query, so this is the query's only resolution.
        public void SubmitPriestAnswer(bool answer)
        {
            if (!_warriorKeepTurn) _turnAttacker = OtherSide(_turnAttacker);
            _warriorKeepTurn = false;
            var extra = new JObject { ["priestAnswerBool"] = FirestoreClient.BoolField(answer) };
            WriteBaseDoc("turn_done", extra);
        }

        // ---------- Wizard ----------

        // Called by the USER of the ability with a digit they already picked and
        // revealed on their own secret (no need to ask -- I know my own secret).
        // Doesn't touch turnAttacker at all: Wizard never costs a turn.
        public void SubmitWizardUse(int myIndex, int myDigit)
        {
            var extra = new JObject
            {
                ["wizardUserSide"] = FirestoreClient.StringField(SideKey(MySide)),
                ["wizardUserIndex"] = FirestoreClient.IntField(myIndex),
                ["wizardUserDigit"] = FirestoreClient.IntField(myDigit)
            };
            WriteBaseDoc("wizard_use", extra);
        }

        // Called by the OTHER side in response to OnMustRevealForWizard, with a
        // digit THEY picked from their own still-hidden positions.
        public void SubmitWizardResponse(int index, int digit)
        {
            var extra = new JObject
            {
                ["wizardResponderSide"] = FirestoreClient.StringField(SideKey(MySide)),
                ["wizardResponderIndex"] = FirestoreClient.IntField(index),
                ["wizardResponderDigit"] = FirestoreClient.IntField(digit)
            };
            WriteBaseDoc("wizard_response", extra);
        }

        // SetDocument REPLACES the whole document, so every write must re-include
        // every persistent field this controller knows about, not just the ones
        // relevant to this particular action -- otherwise each write would quietly
        // erase fields (playerA/playerB included) written by an earlier step.
        private void WriteBaseDoc(string action, JObject extraFields, Action onSuccess = null)
        {
            var fields = new JObject
            {
                ["playerA"] = FirestoreClient.StringField(PlayerA),
                ["playerB"] = FirestoreClient.StringField(PlayerB),
                ["heroA"] = FirestoreClient.StringField(_heroA),
                ["heroB"] = FirestoreClient.StringField(_heroB),
                ["winner"] = FirestoreClient.StringField(_winner),
                ["secretASet"] = FirestoreClient.BoolField(_secretASet),
                ["secretBSet"] = FirestoreClient.BoolField(_secretBSet),
                ["status"] = FirestoreClient.StringField(action == "finished" ? "finished"
                    : _secretASet && _secretBSet ? "in_progress" : "waiting_secrets"),
                ["turnAttacker"] = FirestoreClient.StringField(SideKey(_turnAttacker)),
                ["warriorKeepTurn"] = FirestoreClient.BoolField(_warriorKeepTurn),
                ["action"] = FirestoreClient.StringField(action),
                ["seq"] = FirestoreClient.IntField(_lastSeq + 1)
            };
            if (extraFields != null)
                foreach (var prop in extraFields.Properties())
                    fields[prop.Name] = prop.Value;

            _lastSeq += 1; // optimistic: this write's echo will be ignored by the poll loop
            _db.SetDocument(DocPath, fields, onSuccess, error => OnError?.Invoke(error));
        }

        private static string EncodeGuess(int[] guess) => string.Join("", guess);

        private static int[] DecodeGuess(string s)
        {
            var result = new int[4];
            for (int i = 0; i < 4 && i < s.Length; i++) result[i] = s[i] - '0';
            return result;
        }

        private static JudgeResult DecodeResult(JObject doc, string prefix)
        {
            int strike = (int)FirestoreClient.ReadInt(doc, prefix + "Strike", 0);
            int ball = (int)FirestoreClient.ReadInt(doc, prefix + "Ball", 0);
            return new JudgeResult(strike, ball);
        }
    }
}
