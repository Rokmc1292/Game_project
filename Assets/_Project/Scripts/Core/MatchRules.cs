namespace LiarsBatting.Core
{
    // Match-level rules that don't belong to a single guess. Pure logic (no UnityEngine) so the AI
    // flow, the online flow and any test all ask the same question.
    public static class MatchRules
    {
        // Moving second is a disadvantage, so it comes with a handicap: when the player who moved
        // FIRST guesses the other secret, the second player still gets one last guess. Hitting it too
        // is a draw; missing it hands the first player the win.
        //
        // The one exception is a Warrior who moved first and still holds the ability: that guess wins
        // outright (the Warrior kept the charge instead of spending it on an extra turn).
        // attackerCharges is the attacker's remaining ability charges at the moment of the guess.
        public static bool GivesLastGuess(bool attackerMovedFirst, HeroId attackerHero, int attackerCharges)
        {
            if (!attackerMovedFirst) return false;
            if (attackerHero == HeroId.Warrior && attackerCharges > 0) return false;
            return true;
        }
    }
}
