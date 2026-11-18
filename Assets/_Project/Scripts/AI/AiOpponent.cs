using System;
using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.AI
{
    // How hard the computer plays. Beginner is the original AI, kept exactly as it was.
    public enum AiDifficulty { Beginner, Intermediate, Expert }

    // What the AI did when its secret was guessed: told the truth, or spent a LIE TOKEN.
    public struct DefenseDecision
    {
        public bool Lied;
        public JudgeResult Reported;
    }

    // Rule-based AI -- no API, no ML, just if/else rules and counting.
    //
    // BEGINNER keeps the set of secrets still consistent with everything it has been told about its own
    // guesses, and picks guesses that narrow that set down fastest (classic Mastermind-style minimax).
    // It believes every report it doesn't challenge, and forgets everything when the opponent's lies make
    // the set empty. Its ability use and bluffing are plain dice rolls.
    //
    // INTERMEDIATE and EXPERT keep the same idea but expect to be lied to:
    //  - every secret is scored by how many reports it contradicts, and the AI keeps the best-scoring ones
    //    instead of resetting (a lie costs the truth at most a few contradictions, never all knowledge);
    //  - facts it can be sure of (opened digits, Priest answers, results proven true or false by a
    //    challenge) are applied as hard rules, not as maybe-lies;
    //  - challenges are decided by how plausible the report was, not by a coin flip;
    //  - lies are aimed: the AI models what the player currently believes about its secret and only lies
    //    when the truth would be damaging and a believable alternative exists;
    //  - abilities are used when they are actually worth it.
    // EXPERT additionally weighs the possibilities probabilistically (a Bayes-style posterior with a
    // learned lie rate), looks for the guess that shrinks them the most, and plays around the risk of
    // being forced to open its last hidden digit.
    public class AiOpponent
    {
        public AiDifficulty Difficulty { get; }

        private List<int[]> _candidates;
        private readonly Random _rng;

        // Digits forced open by a trust/challenge call -- unlike a reported strike/
        // ball count these are certain, never lies, so any full candidate-pool reset
        // must re-apply them too instead of forgetting them.
        private readonly int[] _revealedDigits = new int[4];
        private readonly bool[] _revealedMask = new bool[4];
        private bool _guessingPoolExcludes9;

        // Above this size, a full minimax scan (candidates x candidates) is skipped
        // in favor of a random pick, so the AI never stalls the UI on its first move.
        private const int MinimaxCandidateCap = 200;

        // ---- what the smarter levels remember ----
        private struct Observation
        {
            public int[] Guess;
            public JudgeResult Result;
        }

        private static List<int[]> _allSecrets;
        private static List<int[]> AllSecretsCached => _allSecrets ?? (_allSecrets = GenerateAllSecrets());

        private readonly List<Observation> _soft = new List<Observation>();        // reports I trusted: might have been lies
        private readonly List<Observation> _hardTrue = new List<Observation>();    // reports proven true
        private readonly List<Observation> _knownFalse = new List<Observation>();  // reports proven to be lies
        private readonly bool[,] _excludedDigit = new bool[4, 10];                 // Priest said "no" for this digit here
        private int _reportsSeen;
        private int _liesCaught;
        private int _maxOpponentLies = GameState.MaxLieTokens;

        // The cached "possible secrets of the opponent", rebuilt when something new is learned.
        private bool _poolDirty = true;
        private int[][] _pool = new int[0][];
        private double[] _poolWeight = new double[0];
        private double _poolTotal;
        private int _poolBestViolations;

        // What the AI told the player about its own secret, to judge what the player believes now.
        private int[] _ownSecret;
        private bool _ownSecretExcludes9;
        private readonly List<Observation> _given = new List<Observation>();

        // A mismatch between a secret and one trusted report: how much less likely that secret is than
        // one that fits it, when a defender lies with probability ~0.2 and picks among ~12 fake results.
        private const double MismatchWeight = 0.03;

        public AiOpponent() : this(AiDifficulty.Beginner, -1) { }

        public AiOpponent(AiDifficulty difficulty, int seed = -1)
        {
            Difficulty = difficulty;
            _rng = seed >= 0 ? new Random(seed) : new Random();
            _candidates = GenerateAllSecrets();
        }

        private bool Smart => Difficulty != AiDifficulty.Beginner;
        private bool IsExpert => Difficulty == AiDifficulty.Expert;

        public int RemainingCandidateCount
        {
            get
            {
                if (!Smart) return _candidates.Count;
                EnsurePool();
                return _pool.Length;
            }
        }

        public static List<int[]> GenerateAllSecrets()
        {
            var result = new List<int[]>(5040);
            for (int a = 0; a <= 9; a++)
            for (int b = 0; b <= 9; b++)
            {
                if (b == a) continue;
                for (int c = 0; c <= 9; c++)
                {
                    if (c == a || c == b) continue;
                    for (int d = 0; d <= 9; d++)
                    {
                        if (d == a || d == b || d == c) continue;
                        result.Add(new[] { a, b, c, d });
                    }
                }
            }
            return result;
        }

        // exclude9: true when the PLAYER is DemonHunter, restricting the AI's own
        // secret pool to 0~8 (this only ever affects the AI's OWN secret -- the
        // guessing candidate pool below is a separate restriction, see
        // RestrictGuessingPoolExclude9, since DemonHunter restricts the OPPONENT).
        public int[] PickRandomSecret(bool exclude9 = false)
        {
            int[] secret;
            if (Smart)
            {
                // From the full set: the AI being a DemonHunter itself must not rule 9 out of ITS secret.
                var all = AllSecretsCached;
                var pool = exclude9 ? all.FindAll(c => Array.IndexOf(c, 9) < 0) : all;
                secret = CloneFrom(pool);
            }
            else if (!exclude9)
            {
                // From the full set, not from the guessing pool: an AI that is a DemonHunter has had 9 removed
                // from that pool (the PLAYER can't have one), but its OWN secret may still contain a 9.
                secret = CloneFrom(AllSecretsCached);
            }
            else
            {
                var pool = _candidates.FindAll(c => Array.IndexOf(c, 9) < 0);
                secret = CloneFrom(pool);
            }
            _ownSecret = secret;
            _ownSecretExcludes9 = exclude9;
            return (int[])secret.Clone();
        }

        // Tells the smarter levels which hero the player is: a Paladin can heal a LIE TOKEN, so it may
        // have one more lie in it.
        public void SetOpponentHero(HeroId hero)
        {
            _maxOpponentLies = GameState.MaxLieTokens + (hero == HeroId.Paladin ? 1 : 0);
        }

        private int[] CloneRandom() => CloneFrom(_candidates);

        private int[] CloneFrom(List<int[]> pool)
        {
            var src = pool[_rng.Next(pool.Count)];
            return new[] { src[0], src[1], src[2], src[3] };
        }

        // Call once, right after construction, if the AI itself is DemonHunter --
        // the PLAYER's secret is guaranteed to exclude 9, so every future guess
        // (and every future reset) can safely rule out any candidate containing it.
        public void RestrictGuessingPoolExclude9()
        {
            _guessingPoolExcludes9 = true;
            _candidates = FreshCandidates();
            _poolDirty = true;
        }

        // Call after the AI's OWN guess gets a (possibly false) result back that it decided to believe, so
        // its remaining candidate set only contains secrets consistent with that answer.
        public void NarrowByOwnGuess(int[] guess, JudgeResult reportedResult)
        {
            if (Smart)
            {
                _reportsSeen++;
                _soft.Add(new Observation { Guess = (int[])guess.Clone(), Result = reportedResult });
                _poolDirty = true;
                return;
            }

            var next = _candidates.FindAll(c => Judge.Evaluate(c, guess).Equals(reportedResult));
            // If the opponent lied enough to make the candidate set empty, the AI's
            // model of the world is broken -- reset rather than crash. This trades
            // some AI strength for robustness, which is the right call for a first
            // playable build.
            _candidates = next.Count > 0 ? next : FreshCandidates();
        }

        // The report was challenged and turned out to be TRUE (a wrong accusation, or Rogue's insight):
        // unlike a trusted report this is certain.
        public void ObserveConfirmedTruth(int[] guess, JudgeResult reportedResult)
        {
            if (!Smart) { NarrowByOwnGuess(guess, reportedResult); return; }
            _reportsSeen++;
            _hardTrue.Add(new Observation { Guess = (int[])guess.Clone(), Result = reportedResult });
            _poolDirty = true;
        }

        // The report was a lie and the AI caught it: that exact result is certainly NOT the truth.
        // (The beginner AI ignores it, as it always did.)
        public void ObserveLieCaught(int[] guess, JudgeResult reportedResult)
        {
            if (!Smart) return;
            _reportsSeen++;
            _liesCaught++;
            _knownFalse.Add(new Observation { Guess = (int[])guess.Clone(), Result = reportedResult });
            _poolDirty = true;
        }

        // Call when a trust/challenge call forces one of the opponent's digits into
        // the open. Unlike a reported strike/ball count this is certain, never a
        // lie, so it's remembered separately and reapplied even through a reset.
        public void NarrowByRevealedDigit(int index, int digit)
        {
            _revealedMask[index] = true;
            _revealedDigits[index] = digit;
            _poolDirty = true;
            var next = _candidates.FindAll(c => c[index] == digit);
            _candidates = next.Count > 0 ? next : FreshCandidates();
        }

        // The AI's Wizard ability opened one of the player's digits. Only the smarter levels use it
        // (the beginner AI never did).
        public void NoteWizardReveal(int index, int digit)
        {
            if (Smart) NarrowByRevealedDigit(index, digit);
        }

        // The Priest answered "is position X digit Y?" -- always truthfully. Only the smarter levels use it.
        public void NarrowByPriestAnswer(int position, int digit, bool isMatch)
        {
            if (!Smart) return;
            if (isMatch)
            {
                _revealedMask[position] = true;
                _revealedDigits[position] = digit;
            }
            else
            {
                _excludedDigit[position, digit] = true;
            }
            _poolDirty = true;
        }

        private List<int[]> FreshCandidates()
        {
            var all = GenerateAllSecrets();
            if (_guessingPoolExcludes9) all = all.FindAll(c => Array.IndexOf(c, 9) < 0);
            for (int i = 0; i < 4; i++)
                if (_revealedMask[i]) all = all.FindAll(c => c[i] == _revealedDigits[i]);
            return all;
        }

        // =====================================================================================
        //  The smarter levels' model of the player's secret
        // =====================================================================================

        // True when `secret` breaks nothing the AI is SURE of.
        private bool PassesHardFacts(int[] secret)
        {
            if (_guessingPoolExcludes9 && Array.IndexOf(secret, 9) >= 0) return false;
            for (int i = 0; i < 4; i++)
            {
                if (_revealedMask[i] && secret[i] != _revealedDigits[i]) return false;
                if (_excludedDigit[i, secret[i]]) return false;
            }
            foreach (var o in _hardTrue)
                if (!Judge.Evaluate(secret, o.Guess).Equals(o.Result)) return false;
            foreach (var o in _knownFalse)
                if (Judge.Evaluate(secret, o.Guess).Equals(o.Result)) return false;
            return true;
        }

        // Rebuilds the list of possible player secrets with a weight each: 1 for the ones that fit every
        // trusted report, less for the ones that contradict some (the player may have lied).
        private void EnsurePool()
        {
            if (!_poolDirty) return;
            _poolDirty = false;

            var secrets = new List<int[]>();
            var violations = new List<int>();
            int best = int.MaxValue;
            foreach (var s in AllSecretsCached)
            {
                if (!PassesHardFacts(s)) continue;
                int v = 0;
                for (int i = 0; i < _soft.Count; i++)
                    if (!Judge.Evaluate(s, _soft[i].Guess).Equals(_soft[i].Result)) v++;
                secrets.Add(s);
                violations.Add(v);
                if (v < best) best = v;
            }

            if (secrets.Count == 0)
            {
                // Contradictory facts shouldn't happen; fall back to "anything that matches the opened digits".
                foreach (var s in AllSecretsCached)
                {
                    bool ok = true;
                    for (int i = 0; i < 4 && ok; i++)
                        if (_revealedMask[i] && s[i] != _revealedDigits[i]) ok = false;
                    if (ok) { secrets.Add(s); violations.Add(0); }
                }
                best = 0;
            }

            int slack = IsExpert ? 2 : 0;
            var keep = new List<int[]>();
            var weights = new List<double>();
            double total = 0;
            for (int i = 0; i < secrets.Count; i++)
            {
                int extra = violations[i] - best;
                if (extra > slack) continue;
                double w = extra == 0 ? 1.0 : Math.Pow(MismatchWeight, extra);
                keep.Add(secrets[i]);
                weights.Add(w);
                total += w;
            }

            _pool = keep.ToArray();
            _poolWeight = weights.ToArray();
            _poolTotal = total;
            _poolBestViolations = best;
        }

        // Does the AI have reason to think it has been lied to (a caught lie, or reports that don't fit together)?
        private bool SuspectsLies => _liesCaught > 0 || _poolBestViolations > 0;

        // =====================================================================================
        //  Guessing
        // =====================================================================================

        public int[] NextGuess()
        {
            if (!Smart) return NextGuessBeginner();
            EnsurePool();
            return IsExpert ? ExpertGuess() : IntermediateGuess();
        }

        private int[] NextGuessBeginner()
        {
            if (_candidates.Count == 1) return _candidates[0];
            if (_candidates.Count > MinimaxCandidateCap) return CloneRandom();

            int[] best = null;
            int bestWorstCase = int.MaxValue;
            foreach (var guess in _candidates)
            {
                var bucketSizes = new Dictionary<int, int>();
                foreach (var secret in _candidates)
                {
                    var r = Judge.Evaluate(secret, guess);
                    int key = r.Strike * 10 + r.Ball;
                    bucketSizes.TryGetValue(key, out int count);
                    bucketSizes[key] = count + 1;
                }

                int worst = 0;
                foreach (var kv in bucketSizes)
                    if (kv.Value > worst) worst = kv.Value;

                if (worst < bestWorstCase)
                {
                    bestWorstCase = worst;
                    best = guess;
                }
            }
            return best;
        }

        // The one guess it must get right (the last guess of the draw rule): the single most likely secret,
        // not the most informative probe.
        public int[] NextFinalGuess()
        {
            if (!Smart) return NextGuessBeginner();
            EnsurePool();
            double bestW = -1;
            var ties = new List<int>();
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_poolWeight[i] > bestW + 1e-12) { bestW = _poolWeight[i]; ties.Clear(); ties.Add(i); }
                else if (Math.Abs(_poolWeight[i] - bestW) <= 1e-12) ties.Add(i);
            }
            return (int[])_pool[ties[_rng.Next(ties.Count)]].Clone();
        }

        // Minimax over a sample of the possibilities, like the beginner's but also used while there are
        // hundreds of them (the beginner just guesses at random then).
        private int[] IntermediateGuess()
        {
            int n = _pool.Length;
            if (n == 1) return (int[])_pool[0].Clone();

            var guessIdx = SampleIndices(n, 120);
            var evalIdx = SampleIndices(n, 250);
            var bucket = new int[25];

            int bestWorst = int.MaxValue;
            var ties = new List<int>();
            foreach (int g in guessIdx)
            {
                Array.Clear(bucket, 0, bucket.Length);
                foreach (int e in evalIdx)
                {
                    var r = Judge.Evaluate(_pool[e], _pool[g]);
                    bucket[r.Strike * 5 + r.Ball]++;
                }
                int worst = 0;
                for (int k = 0; k < bucket.Length; k++) if (bucket[k] > worst) worst = bucket[k];
                if (worst < bestWorst) { bestWorst = worst; ties.Clear(); ties.Add(g); }
                else if (worst == bestWorst) ties.Add(g);
            }
            return (int[])_pool[ties[_rng.Next(ties.Count)]].Clone();
        }

        // Picks the guess with the smallest EXPECTED weight left over, over the weighted possibilities.
        private int[] ExpertGuess()
        {
            int n = _pool.Length;
            if (n == 1) return (int[])_pool[0].Clone();

            // Guess pool: the likeliest secrets plus a random spread. Evaluation set: a weighted-sample-free
            // uniform sample (weights are carried along), so the cost stays bounded however large the pool is.
            var guessIdx = new List<int>();
            if (n <= 160)
            {
                for (int i = 0; i < n; i++) guessIdx.Add(i);
            }
            else
            {
                var order = new List<int>(n);
                for (int i = 0; i < n; i++) order.Add(i);
                order.Sort((a, b) => _poolWeight[b].CompareTo(_poolWeight[a]));
                for (int i = 0; i < 80; i++) guessIdx.Add(order[i]);
                var rest = order.GetRange(80, n - 80);
                Shuffle(rest);
                for (int i = 0; i < 80 && i < rest.Count; i++) guessIdx.Add(rest[i]);
            }
            var evalIdx = SampleIndices(n, 350);

            var bucket = new double[25];
            double bestScore = double.MaxValue;
            var ties = new List<int>();
            foreach (int g in guessIdx)
            {
                Array.Clear(bucket, 0, bucket.Length);
                double total = 0;
                foreach (int e in evalIdx)
                {
                    var r = Judge.Evaluate(_pool[e], _pool[g]);
                    bucket[r.Strike * 5 + r.Ball] += _poolWeight[e];
                    total += _poolWeight[e];
                }
                if (total <= 0) continue;
                double score = 0;
                for (int k = 0; k < bucket.Length; k++) score += bucket[k] * bucket[k];
                score /= total;
                // A guess that is itself a possible secret can end the game on the spot: nudge towards it.
                score -= 0.35 * _poolWeight[g];
                if (score < bestScore - 1e-9) { bestScore = score; ties.Clear(); ties.Add(g); }
                else if (Math.Abs(score - bestScore) <= 1e-9) ties.Add(g);
            }
            if (ties.Count == 0) return (int[])_pool[0].Clone();
            return (int[])_pool[ties[_rng.Next(ties.Count)]].Clone();
        }

        private List<int> SampleIndices(int n, int max)
        {
            var all = new List<int>(n);
            for (int i = 0; i < n; i++) all.Add(i);
            if (n <= max) return all;
            Shuffle(all);
            return all.GetRange(0, max);
        }

        private void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                int tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        // =====================================================================================
        //  Defending (is my secret being guessed: tell the truth or spend a LIE TOKEN?)
        // =====================================================================================

        // Defender-side bluff decision: the AI's own secret is being guessed, and it
        // must decide whether to lie about the true result before reporting it.
        // canLieAboutWin is Hunter's passive: everyone else must report a true
        // 4-strike honestly no matter how many tokens they have left.
        public bool ShouldLieAsDefender(JudgeResult trueResult, int lieTokensLeft, bool canLieAboutWin = false)
        {
            if (lieTokensLeft <= 0) return false;
            if (trueResult.IsWin) return canLieAboutWin && _rng.NextDouble() < 0.7;
            if (trueResult.Strike >= 2) return _rng.NextDouble() < 0.5;
            return _rng.NextDouble() < 0.15;
        }

        // Returns a plausible-but-false (strike, ball) pair, never a fake win.
        public JudgeResult FabricateResult(JudgeResult trueResult)
        {
            var options = Judge.PlausibleFakeResults(trueResult);
            return options[_rng.Next(options.Count)];
        }

        // The full defense: what to report for `guess`. ownRevealed = which of the AI's own digits are open.
        public DefenseDecision DecideDefense(int[] guess, JudgeResult trueResult, int lieTokensLeft,
            bool canLieAboutWin, bool[] ownRevealed)
        {
            if (!Smart || _ownSecret == null)
            {
                bool lied = ShouldLieAsDefender(trueResult, lieTokensLeft, canLieAboutWin);
                return new DefenseDecision { Lied = lied, Reported = lied ? FabricateResult(trueResult) : trueResult };
            }

            var decision = SmartDefense(guess, trueResult, lieTokensLeft, canLieAboutWin, ownRevealed);
            _given.Add(new Observation { Guess = (int[])guess.Clone(), Result = decision.Reported });
            return decision;
        }

        // The secrets the player can currently believe mine might be, going by what I have told them and
        // which of my digits are open.
        private List<int[]> PlayerView(bool[] ownRevealed)
        {
            var view = new List<int[]>();
            foreach (var s in AllSecretsCached)
            {
                if (_ownSecretExcludes9 && Array.IndexOf(s, 9) >= 0) continue;
                bool ok = true;
                for (int i = 0; i < 4 && ok; i++)
                    if (ownRevealed != null && ownRevealed[i] && s[i] != _ownSecret[i]) ok = false;
                for (int i = 0; i < _given.Count && ok; i++)
                    if (!Judge.Evaluate(s, _given[i].Guess).Equals(_given[i].Result)) ok = false;
                if (ok) view.Add(s);
            }
            return view;
        }

        private static int HiddenCount(bool[] revealed)
        {
            if (revealed == null) return 4;
            int n = 0;
            foreach (bool r in revealed) if (!r) n++;
            return n;
        }

        private DefenseDecision SmartDefense(int[] guess, JudgeResult trueResult, int lieTokensLeft,
            bool canLieAboutWin, bool[] ownRevealed)
        {
            var truth = new DefenseDecision { Lied = false, Reported = trueResult };
            if (lieTokensLeft <= 0) return truth;

            // Hunter hiding a real 4-strike: the only way to stay alive, so do it.
            if (trueResult.IsWin)
            {
                if (!canLieAboutWin) return truth;
                if (!IsExpert && _rng.NextDouble() >= 0.85) return truth;
                return Lie(guess, trueResult, null);
            }

            // Being caught means opening a digit; with one hidden digit left that loses the game.
            int hidden = HiddenCount(ownRevealed);
            if (hidden <= 1) return truth;

            var view = PlayerView(ownRevealed);
            if (view.Count == 0) return truth;

            // How many of the secrets the player considers possible give each result for this guess.
            var counts = new int[25];
            foreach (var s in view)
            {
                var r = Judge.Evaluate(s, guess);
                counts[r.Strike * 5 + r.Ball]++;
            }
            int remainingIfTruth = counts[trueResult.Strike * 5 + trueResult.Ball];

            // The most believable false answer: the one the player finds most plausible already.
            var fakes = Judge.PlausibleFakeResults(trueResult);
            var bestFakes = new List<JudgeResult>();
            int bestCount = 0;
            foreach (var f in fakes)
            {
                int c = counts[f.Strike * 5 + f.Ball];
                if (c > bestCount) { bestCount = c; bestFakes.Clear(); bestFakes.Add(f); }
                else if (c == bestCount && c > 0) bestFakes.Add(f);
            }
            double believability = (double)bestCount / view.Count;

            double p;
            if (IsExpert)
            {
                // The fewer secrets the truth leaves, the more it gives away -- and the more a lie is worth.
                if (believability < 0.03) return truth;
                if (remainingIfTruth <= 2) p = 0.95;
                else if (remainingIfTruth <= 6) p = 0.75;
                else if (remainingIfTruth <= 20) p = 0.45;
                else if (remainingIfTruth <= 80) p = 0.25;
                else p = 0.08;
                if (trueResult.Strike >= 3) p = Math.Max(p, 0.9);
                if (lieTokensLeft == 1) p *= 0.75;   // keep the last token for a moment that matters more
                if (hidden == 2) p *= 0.8;
            }
            else
            {
                if (bestCount == 0) return truth;
                p = trueResult.Strike >= 3 ? 0.8 : trueResult.Strike == 2 ? 0.5 : 0.18;
            }

            if (_rng.NextDouble() >= p) return truth;
            if (bestFakes.Count == 0) return truth;
            var fake = bestFakes[_rng.Next(bestFakes.Count)];
            return new DefenseDecision { Lied = true, Reported = fake };
        }

        private DefenseDecision Lie(int[] guess, JudgeResult trueResult, List<JudgeResult> preferred)
        {
            var options = preferred != null && preferred.Count > 0 ? preferred : Judge.PlausibleFakeResults(trueResult);
            return new DefenseDecision { Lied = true, Reported = options[_rng.Next(options.Count)] };
        }

        // =====================================================================================
        //  Attacking (the opponent told me a result: believe it or call it a lie?)
        // =====================================================================================

        // Paladin's passive-use heuristic: heal as soon as it would actually help.
        public bool ShouldUsePaladinHeal(int lieTokensLeft, int charges)
            => charges > 0 && lieTokensLeft == 0;

        // How likely it is that `reported` is a lie, given everything known so far (smarter levels only).
        // 1 = it contradicts what is certain, 0 = the opponent can't be lying any more.
        private double LieProbability(int[] guess, JudgeResult reported, out double fit)
        {
            EnsurePool();
            double match = 0;
            for (int i = 0; i < _pool.Length; i++)
                if (Judge.Evaluate(_pool[i], guess).Equals(reported)) match += _poolWeight[i];
            fit = _poolTotal > 0 ? match / _poolTotal : 0;

            if (reported.IsWin) { return 0; }
            if (fit <= 0) return 1;

            double prior = IsExpert
                ? Math.Min(0.45, Math.Max(0.08, (_liesCaught + 0.6) / (_reportsSeen + 3.0)))
                : 0.2;
            if (_liesCaught >= _maxOpponentLies) prior = 0;   // out of lies: this has to be the truth
            if (prior <= 0) return 0;

            // P(report | lie) ~ (1 - fit) spread over ~12 fake results; P(report | truth) = fit.
            double lie = prior * (1 - fit) / 12.0;
            double honest = (1 - prior) * fit;
            return lie / (lie + honest);
        }

        // Attacker-side call: should the AI treat this reported result as a lie worth calling out?
        // myHiddenDigits: how many of the AI's own digits are still hidden -- a wrong accusation opens one.
        public bool ShouldChallengeAsAttacker(int[] guess, JudgeResult reportedResult, int myHiddenDigits = 4)
        {
            if (!Smart) return ShouldChallengeBeginner(guess, reportedResult);

            double pLie = LieProbability(guess, reportedResult, out double fit);
            if (pLie >= 0.999) return true;   // impossible given what is certain: it MUST be a lie

            if (IsExpert)
            {
                double threshold = myHiddenDigits >= 3 ? 0.5 : myHiddenDigits == 2 ? 0.58 : 0.85;
                if (Math.Abs(pLie - threshold) < 0.05) return _rng.NextDouble() < 0.5;
                return pLie > threshold;
            }

            if (myHiddenDigits <= 1) return false;   // a wrong guess would open my last digit
            if (fit < 0.03) return _rng.NextDouble() < 0.8;
            if (fit < 0.1) return _rng.NextDouble() < 0.45;
            if (fit < 0.25) return _rng.NextDouble() < 0.18;
            return _rng.NextDouble() < 0.04;
        }

        // Beginner: should the AI treat this reported result as a lie worth calling out, based on how few
        // of its remaining candidates for the opponent's secret would actually produce it? A result only a
        // handful of candidates could produce is the tell -- a real defender's answer is always consistent
        // with at least one true secret, but a fabricated one often isn't consistent with much of anything
        // the AI has narrowed down to.
        private bool ShouldChallengeBeginner(int[] guess, JudgeResult reportedResult)
        {
            if (_candidates.Count == 0) return false;
            int consistent = 0;
            foreach (var c in _candidates)
                if (Judge.Evaluate(c, guess).Equals(reportedResult)) consistent++;
            double ratio = (double)consistent / _candidates.Count;

            if (ratio < 0.05) return _rng.NextDouble() < 0.6;
            if (ratio < 0.2) return _rng.NextDouble() < 0.25;
            return _rng.NextDouble() < 0.05; // small baseline chance even when it looks plausible
        }

        // Picks a still-hidden digit position to open when the AI must reveal one.
        // Returns -1 if every position is already revealed. Also doubles as
        // "pick an open position" for Priest's query and Wizard's free reveal --
        // same operation, different callers.
        public int PickRevealIndex(bool[] revealed)
        {
            var open = new List<int>();
            for (int i = 0; i < revealed.Length; i++)
                if (!revealed[i]) open.Add(i);
            return open.Count > 0 ? open[_rng.Next(open.Count)] : -1;
        }

        // Which of the AI's OWN hidden digits to open when it is forced to. The expert opens the one the
        // player already half-knows (so it costs the least); everyone else picks at random.
        public int PickOwnRevealIndex(bool[] ownRevealed)
        {
            if (!IsExpert || _ownSecret == null) return PickRevealIndex(ownRevealed);

            var view = PlayerView(ownRevealed);
            if (view.Count == 0) return PickRevealIndex(ownRevealed);

            var ties = new List<int>();
            double bestShare = -1;
            for (int i = 0; i < ownRevealed.Length; i++)
            {
                if (ownRevealed[i]) continue;
                int same = 0;
                foreach (var s in view) if (s[i] == _ownSecret[i]) same++;
                double share = (double)same / view.Count;
                if (share > bestShare + 1e-9) { bestShare = share; ties.Clear(); ties.Add(i); }
                else if (Math.Abs(share - bestShare) <= 1e-9) ties.Add(i);
            }
            return ties.Count > 0 ? ties[_rng.Next(ties.Count)] : -1;
        }

        // ---- hero abilities ----

        // Rogue: purely informational and risk-free, so the beginner uses it whenever available. The
        // smarter levels keep the single charge for a report they can't judge for themselves.
        public bool ShouldUseRogueDetect(int charges) => charges > 0;

        public bool ShouldUseRogueDetect(int charges, int[] guess, JudgeResult reported)
        {
            if (charges <= 0) return false;
            if (!Smart) return true;
            double p = LieProbability(guess, reported, out _);
            return p > 0.15 && p < 0.9;
        }

        // Priest: worth a charge when there's still real uncertainty to resolve. The expert mostly saves it
        // for when it suspects it has been lied to, since a Priest answer can't be a lie.
        public bool ShouldUsePriestQuery(int charges)
        {
            if (charges <= 0) return false;
            if (!Smart) return _candidates.Count > 30 && _rng.NextDouble() < 0.35;

            EnsurePool();
            if (!IsExpert) return _pool.Length > 25 && _rng.NextDouble() < 0.5;
            if (_pool.Length <= 3) return false;
            if (SuspectsLies) return _rng.NextDouble() < 0.75;
            return _pool.Length >= 10 && _pool.Length <= 80 && _rng.NextDouble() < 0.2;
        }

        // Priest asks "is position X digit Y?" -- the beginner picks a still-hidden position and the digit
        // most common there. The smarter levels ask the question whose yes/no splits the possibilities
        // closest to half and half. Returns (-1, -1) if every position is already revealed.
        public (int position, int digit) PickPriestQuery(bool[] revealed)
        {
            if (Smart)
            {
                EnsurePool();
                var counts = new double[4, 10];
                for (int i = 0; i < _pool.Length; i++)
                    for (int pos = 0; pos < 4; pos++)
                        counts[pos, _pool[i][pos]] += _poolWeight[i];

                double half = _poolTotal / 2;
                double bestGap = double.MaxValue;
                var ties = new List<(int, int)>();
                for (int pos = 0; pos < 4; pos++)
                {
                    if (revealed[pos]) continue;
                    for (int d = 0; d <= 9; d++)
                    {
                        if (counts[pos, d] <= 0) continue;
                        double gap = Math.Abs(counts[pos, d] - half);
                        if (gap < bestGap - 1e-9) { bestGap = gap; ties.Clear(); ties.Add((pos, d)); }
                        else if (Math.Abs(gap - bestGap) <= 1e-9) ties.Add((pos, d));
                    }
                }
                if (ties.Count > 0) return ties[_rng.Next(ties.Count)];
            }

            int position = PickRevealIndex(revealed);
            if (position < 0) return (-1, -1);

            var common = new int[10];
            foreach (var c in _candidates) common[c[position]]++;
            int bestDigit = 0;
            for (int d = 1; d <= 9; d++)
                if (common[d] > common[bestDigit]) bestDigit = d;
            return (position, bestDigit);
        }

        // Warrior: worth burning the once-per-game charge when a second guess in a row is likely to close
        // the game out. A Warrior that moved FIRST keeps the charge instead (the smarter levels): holding
        // it turns a correct guess into an immediate win, with no last guess for the opponent.
        public bool ShouldUseWarriorExtraTurn(int charges, bool movedFirst = false)
        {
            if (charges <= 0) return false;
            if (!Smart) return _candidates.Count <= 5;
            if (movedFirst) return false;
            EnsurePool();
            return _pool.Length <= (IsExpert ? 10 : 6);
        }

        // Wizard: a free, no-downside peek -- use it as soon as it's available.
        public bool ShouldUseWizardReveal(int charges) => charges > 0;
    }
}
