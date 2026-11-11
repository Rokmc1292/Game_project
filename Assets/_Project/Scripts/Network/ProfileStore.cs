using System;
using LiarsBatting.Core;
using LiarsBatting.Progression;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LiarsBatting.Network
{
    // Reads and writes the signed-in player's own document, players/{nickname}. Only the owner's
    // client ever writes it, so a cached copy (Current) plus field-level patches is safe: two
    // writes from this one client can't race each other into a lost update.
    //
    // Fields: nickname, lastSeen (existing), profileIcon (HeroId name), wins, losses.
    // Older documents simply lack the new fields and read back as the defaults.
    public class ProfileStore
    {
        private const string Collection = "players";

        private readonly FirestoreClient _db;

        public PlayerProfile Current { get; private set; }

        public ProfileStore(FirestoreClient db)
        {
            _db = db;
        }

        private static string PathOf(string nickname) => $"{Collection}/{nickname}";

        public void Load(string nickname, Action<PlayerProfile> onLoaded, Action<string> onError)
        {
            _db.GetDocument(PathOf(nickname), doc =>
            {
                var profile = new PlayerProfile { Nickname = nickname };
                if (doc != null)
                {
                    string icon = FirestoreClient.ReadString(doc, "profileIcon", "");
                    if (Enum.TryParse(icon, out HeroId hero)) profile.ProfileIcon = hero;
                    profile.Wins = (int)FirestoreClient.ReadInt(doc, "wins", 0);
                    profile.Losses = (int)FirestoreClient.ReadInt(doc, "losses", 0);
                }
                Current = profile;
                onLoaded?.Invoke(profile);
            }, onError);
        }

        // Runs `action` against Current, loading it first if it isn't this player's yet.
        private void WithCurrent(string nickname, Action<PlayerProfile> action, Action<string> onError)
        {
            if (Current != null && Current.Nickname == nickname) { action(Current); return; }
            Load(nickname, action, onError);
        }

        public void SaveProfileIcon(string nickname, HeroId icon, Action onSaved, Action<string> onError)
        {
            WithCurrent(nickname, profile =>
            {
                var fields = new JObject { ["profileIcon"] = FirestoreClient.StringField(icon.ToString()) };
                _db.PatchFields(PathOf(nickname), fields, new[] { "profileIcon" }, () =>
                {
                    profile.ProfileIcon = icon;
                    onSaved?.Invoke();
                }, onError);
            }, onError);
        }

        // One finished match against a person (surrenders included) -- never called for AI matches.
        public void RecordHumanMatch(string nickname, bool won)
        {
            WithCurrent(nickname, profile =>
            {
                if (won) profile.Wins++; else profile.Losses++;
                var fields = new JObject
                {
                    ["wins"] = FirestoreClient.IntField(profile.Wins),
                    ["losses"] = FirestoreClient.IntField(profile.Losses),
                    ["lastSeen"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };
                _db.PatchFields(PathOf(nickname), fields, new[] { "wins", "losses", "lastSeen" },
                    null, error => Debug.LogWarning($"전적 저장 실패: {error}"));
            }, error => Debug.LogWarning($"전적 저장 실패(프로필 읽기): {error}"));
        }

        // Moves the whole document to the new name: copy every field to players/{newName}, then
        // delete players/{oldName}, so the old nickname is freed instead of staying taken forever.
        public void Rename(string oldName, string newName, Action onRenamed, Action<string> onError)
        {
            _db.GetDocument(PathOf(newName), existing =>
            {
                if (existing != null) { onError?.Invoke("이미 사용 중인 닉네임입니다."); return; }

                _db.GetDocument(PathOf(oldName), oldDoc =>
                {
                    var fields = oldDoc?["fields"] as JObject ?? new JObject();
                    fields["nickname"] = FirestoreClient.StringField(newName);
                    fields["lastSeen"] = FirestoreClient.IntField(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                    _db.SetDocument(PathOf(newName), fields, () =>
                    {
                        if (Current != null && Current.Nickname == oldName) Current.Nickname = newName;
                        // The new document is the one that matters; a failed delete only leaves the
                        // old name reserved, so it is logged rather than reported as a failed rename.
                        _db.DeleteDocument(PathOf(oldName), null,
                            error => Debug.LogWarning($"이전 닉네임 문서 삭제 실패: {error}"));
                        onRenamed?.Invoke();
                    }, error => onError?.Invoke($"변경에 실패했습니다: {error}"));
                }, error => onError?.Invoke($"서버 연결에 실패했습니다: {error}"));
            }, error => onError?.Invoke($"서버 연결에 실패했습니다: {error}"));
        }
    }
}
