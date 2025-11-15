namespace LouveSystems.K2.BotLib
{
    using LouveSystems.K2.Lib;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public abstract class ComputerPlayerBehaviour
    {
        public const ushort MAX_TICKS_ACTIONS = 1200; // For 8 players, 30 seconds per turn, 60FPS, this makes sense
        public const byte MAX_TICKS_LATE_ACTIONS = 20;
        public const byte MINIMUM_PERSONAS = 8;

        public abstract string GetInternalName();

        public abstract IReadOnlyList<ComputerPersona> GetPersonas();

        /// Each CPU has 200 ticks tops to take all actions. Return TRUE if no more computation is needed
        public abstract IEnumerator<bool> TakeActions(byte forPlayerIndex, GameSession session);

        /// Each CPU has 20 ticks tops to take late actions. Return TRUE if no more computation is needed
        public virtual IEnumerator<bool> TakeActionsLate(byte forPlayerIndex, GameSession session)
        {
            yield return true;
        }
    }
}
