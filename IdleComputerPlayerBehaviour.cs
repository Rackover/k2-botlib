namespace LouveSystems.K2.BotLib
{
    using LouveSystems.K2.Lib;
    using System.Collections.Generic;

    public sealed class IdleComputerPlayerBehaviour : ComputerPlayerBehaviour
    {
        public override string GetInternalName()
        {
            return "none";
        }

        public override IReadOnlyList<ComputerPersona> GetPersonas()
        {
            ComputerPersona[] personas = new ComputerPersona[MINIMUM_PERSONAS];

            for (int i = 0; i < personas.Length; i++) {
                personas[i].name = $"{nameof(IdleComputerPlayerBehaviour)}{i}";
            }

            return personas;
        }

        public override IEnumerator<bool> TakeActions(byte forPlayerIndex, GameSession session)
        {
            yield return true;
        }
    }
}
