using MelonLoader;

[assembly: MelonInfo(typeof(Mai2DRPC.Core), "Mai2DRPC", "1.0.0", "michioxd", null)]
[assembly: MelonGame("sega-interactive", "Sinmai")]

namespace Mai2DRPC
{
    public class Core : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Initialized.");
        }
    }
}
