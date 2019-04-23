namespace KerbalHealth
{
    public class ConnectedFactor : HealthFactor
    {
        public override string Name => "Connected";

        public override double BaseChangePerDay => HighLogic.CurrentGame.Parameters.CustomParams<KerbalHealthFactorsSettings>().ConnectedFactor;

        public override double ChangePerDay(ProtoCrewMember pcm)
        {
            if (Core.IsInEditor) return IsEnabledInEditor() ? BaseChangePerDay : 0;
            if (RemoteTech.API.API.IsRemoteTechEnabled())
            {
                return (Core.IsKerbalLoaded(pcm) && (RemoteTech.API.API.HasConnectionToKSC(Core.KerbalVessel(pcm).id))) ? BaseChangePerDay : 0;

            }
            else
            {
                return (Core.IsKerbalLoaded(pcm) && (Core.KerbalVessel(pcm).Connection != null) && Core.KerbalVessel(pcm).Connection.IsConnectedHome) ? BaseChangePerDay : 0;

            }
        }
    }
}
