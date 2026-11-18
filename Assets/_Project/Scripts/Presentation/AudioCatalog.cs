using UnityEngine;

namespace LiarsBatting.Presentation
{
    // The editor populates this asset with references to the user's Audio folder.
    public class AudioCatalog : ScriptableObject
    {
        public AudioClip[] Clips;

        public static readonly System.Collections.Generic.IReadOnlyList<string> RequiredNames =
            System.Array.AsReadOnly(new[]
            {
                "BGM_Lobby_Noir", "BGM_Match_Tension", "BGM_Match_Climax",
                "BGM_Win_Stinger", "BGM_Lose_Stinger", "BGM_Draw_Stinger", "BGM_Match_FinalChance",
                "SFX_UI_Hover", "SFX_UI_Click", "SFX_Match_Found", "SFX_Invite_Notification",
                "SFX_Card_Hover", "SFX_Card_Select", "SFX_Card_Deselect", "SFX_Guess_Submit",
                "SFX_Response_Received", "SFX_Judge_Strike", "SFX_Judge_Ball", "SFX_Judge_Out",
                "SFX_LieToken_Spend", "SFX_Truth_Declare", "SFX_Trust_Accept", "SFX_Challenge_Doubt",
                "SFX_Reveal_Busted", "SFX_Reveal_Punished",
                "SFX_Hero_Paladin_Restore", "SFX_Hero_Warrior_Rush", "SFX_Hero_Wizard_Clairvoyance",
                "SFX_Hero_Priest_Inquiry", "SFX_Hero_Rogue_Insight",
                "SFX_Turn_Start_My", "SFX_Turn_Start_Opponent", "SFX_Timer_Tick_Urgent", "SFX_Timer_Timeout",
                "SFX_Result_Dim_Drop", "SFX_Result_Stamp_Win", "SFX_Result_Stamp_Lose", "SFX_Result_Card_Reveal",
                "SFX_Result_Stamp_Draw", "SFX_LastGuess_Start", "SFX_Secret_Lock", "SFX_Digit_Reveal",
                "SFX_Match_Start", "SFX_Hero_Select_Confirm", "SFX_Hero_DemonHunter_Seal",
                "SFX_Warrior_ExtraTurn", "SFX_Priest_Answer_Yes", "SFX_Priest_Answer_No",
                "SFX_Rematch_Requested", "SFX_Rematch_Ready", "SFX_UI_Error", "SFX_Cosmetic_Equip",
                "SFX_Progress_LevelUp", "SFX_Progress_HeroLevelUp",
                "SFX_Progress_ChallengeComplete", "SFX_Progress_RewardUnlock"
            });
    }
}
