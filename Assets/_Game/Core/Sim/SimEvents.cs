namespace Starsoil.Core
{
    /// <summary>
    /// Typed sim events raised during a tick and consumed by presentation/UI after the tick
    /// (docs/plan/08 event bus; M0 keeps a simple per-tick list on World).
    /// </summary>
    public interface ISimEvent
    {
    }

    public sealed class BuildingPlacedEvent : ISimEvent
    {
        public int BuildingId;
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;
    }

    public sealed class BuildingRemovedEvent : ISimEvent
    {
        public int BuildingId;
    }

    public sealed class CommandRejectedEvent : ISimEvent
    {
        public string Reason;
    }
}
