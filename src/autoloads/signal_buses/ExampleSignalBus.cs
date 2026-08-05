using System;
using Godot;

namespace GodotDotnetTraining
{
    public partial class ExampleSignalBus : Node
    {
        // Insert variant-type parameters here.
        // Signals must have the "*EventHandler" suffix.
        [Signal]
        public delegate void ExampleEventHandler(int value);

        /*
        Usage: ExampleSignalBus.EmitExample();
        */
        public static void EmitExample(int value)
        {
            Instance.EmitSignal(nameof(Example), value);
        }

        public static ExampleSignalBus Instance { get; set; }

        public override void _Ready()
        {
            Instance = this;
        }
    }
}
