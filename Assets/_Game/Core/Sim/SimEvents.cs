namespace Starsoil.Core
{
    /// <summary>
    /// Typed sim events raised during a tick and consumed by presentation/UI after the tick
    /// (docs/plan/08 event bus).
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

    public sealed class BlueprintPlacedEvent : ISimEvent
    {
        public int BlueprintId;
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;
    }

    public sealed class BlueprintRemovedEvent : ISimEvent
    {
        public int BlueprintId;
    }

    public sealed class CommandRejectedEvent : ISimEvent
    {
        public string Reason;
    }

    public sealed class CraftCompletedEvent : ISimEvent
    {
        public int StationId;
        public string RecipeId;
    }

    public sealed class NodeDepletedEvent : ISimEvent
    {
        public int NodeId;
        public int X;
        public int Y;
    }

    public sealed class ColonistDiedEvent : ISimEvent
    {
        public int ColonistId;
        public DeathCause Cause;
    }

    public sealed class ColonistFaintedEvent : ISimEvent
    {
        public int ColonistId;
    }

    public sealed class ColonistBuriedEvent : ISimEvent
    {
        public int X;
        public int Y;
    }

    public sealed class AlertRaisedEvent : ISimEvent
    {
        public string AlertId;
        public AlertSeverity Severity;
        public int X;
        public int Y;
    }

    public sealed class AlertClearedEvent : ISimEvent
    {
        public string AlertId;
    }

    public sealed class StormForecastEvent : ISimEvent
    {
        public long StartTick;
    }

    public sealed class StormStartedEvent : ISimEvent
    {
        public long EndTick;
    }

    public sealed class StormEndedEvent : ISimEvent
    {
    }

    public sealed class TutorialAdvancedEvent : ISimEvent
    {
        public int StepIndex;
        public string NextStepId;
    }

    public sealed class DefeatEvent : ISimEvent
    {
        public long DaysSurvived;
    }
}
