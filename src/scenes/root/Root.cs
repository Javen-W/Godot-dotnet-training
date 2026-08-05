using Godot;
using System;

namespace GodotDotnetTraining
{
	public partial class Root : Node3D
	{
		private SignalConnectionManager _signals;

		// Called when the node enters the scene tree for the first time.
		public override void _Ready()
		{
			Logger.Info("Root scene ready");
			HandleSignals();

			// Emit an example signal.
			ExampleSignalBus.EmitExample(777);

			// Create an example item instance.
			var itemData = ItemDataFactory.CreateItemData(ItemID.EXAMPLE_ITEM);
			Logger.Info($"Item data created: {itemData.Name}");

			// Create an example hex tile instance.
			foreach (var axialCoord in HexMath.AxialSpiral(Vector2I.Zero, 2))
			{
				var hexTile = new HexTile()
				{
					Size = 1.0f,
					AxialCoord = axialCoord,
					// BaseType = HexTileBaseType.Grass,
				};
				AddChild(hexTile);
			}
		}

		// Called every frame. 'delta' is the elapsed time since the previous frame.
		public override void _Process(double delta)
		{
		}

		// Set up signal connections.
		private void HandleSignals()
		{
			_signals = new SignalConnectionManager(this);

			// Connect our OnExample() method to ExampleSignalBus.Example signal events.
			// All handlers are automatically disconnected when the node exits the tree to avoid memory leaks.
			_signals.Connect(
				() => ExampleSignalBus.Instance.Example += OnExample,
				() => ExampleSignalBus.Instance.Example -= OnExample);
		}

		private void OnExample(int value)
		{
			Logger.Info($"Example signal received: {value}");
		}
	}
}

