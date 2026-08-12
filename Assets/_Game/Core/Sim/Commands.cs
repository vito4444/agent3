using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// All player intents enter the sim as commands applied at the start of a tick;
    /// UI never mutates sim state directly (docs/plan/08).
    /// </summary>
    public interface ICommand
    {
        void Execute(World world);
    }

    public sealed class CommandQueue
    {
        private readonly Queue<ICommand> _pending = new Queue<ICommand>();

        public int PendingCount => _pending.Count;

        public void Enqueue(ICommand command)
        {
            _pending.Enqueue(command);
        }

        internal void Drain(World world)
        {
            while (_pending.Count > 0)
            {
                _pending.Dequeue().Execute(world);
            }
        }
    }

    public sealed class PlaceBuildingCommand : ICommand
    {
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;

        public void Execute(World world)
        {
            int id = world.Buildings.Place(DefId, X, Y, Rotation, out PlacementError error);
            if (error == PlacementError.None)
            {
                world.Events.Add(new BuildingPlacedEvent
                {
                    BuildingId = id,
                    DefId = DefId,
                    X = X,
                    Y = Y,
                    Rotation = Rotation
                });
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "place:" + error });
            }
        }
    }

    public sealed class RemoveBuildingCommand : ICommand
    {
        public int BuildingId;

        public void Execute(World world)
        {
            if (world.Buildings.Remove(BuildingId))
            {
                world.Events.Add(new BuildingRemovedEvent { BuildingId = BuildingId });
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "remove:not_found" });
            }
        }
    }
}
