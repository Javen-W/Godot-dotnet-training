using Godot;
using Godot.Collections;

namespace GodotDotnetTraining
{
	/// <summary>
	/// Coordinates a set of child <see cref="State"/> nodes and keeps one active at a time.
	/// It owns enter/exit delegation, emits a generic transition signal for listeners, and exposes a replicated
	/// current-state name so gameplay systems can synchronize state authority over multiplayer without bespoke enums.
	/// </summary>
	[GlobalClass]
	public partial class StateMachine : Node
	{
		/// <summary>
		/// Raised after the machine swaps from one child state to another.
		/// Gameplay systems can subscribe here to react to generic state authority changes without hard-coding scene-specific replication glue.
		/// </summary>
		[Signal]
		public delegate void StateChangedEventHandler(string previousStateName, string currentStateName);

		/// <summary>
		/// The currently active child state node.
		/// Scenes author the initial value here, and runtime transitions keep it aligned with <see cref="CurrentStateName"/>.
		/// </summary>
		[Export]
		public State CurrentState;

		/// <summary>
		/// Gets or sets the active child-state name for runtime and replicated transitions.
		/// Multiplayer synchronizers can replicate this generic property so peers follow the same authored state graph
		/// instead of introducing gameplay-specific mirror enums.
		/// </summary>
		[Export]
		public string CurrentStateName
		{
			get => CurrentState?.Name ?? _pendingStateName ?? string.Empty;
			set => SetCurrentState(value, new Dictionary(), deferUntilReady: true);
		}

		private Dictionary<string, State> _states;
		private SignalConnectionManager _signals;
		private string _pendingStateName = string.Empty;
		private bool _isReady;

		/// <summary>
		/// Caches child states, wires their transition requests, and enters the configured initial state.
		/// Any replicated state name received before readiness is honored here so spawned peers begin in the same
		/// authored state as the host without needing scene-specific initialization code.
		/// </summary>
		public override void _Ready()
		{
			_states = new Dictionary<string, State>();
			_signals = new SignalConnectionManager(this);

			foreach (var child in GetChildren())
			{
				if (child is not State state)
				{
					Logger.Warning($"Child '{child.Name}' is not a State.");
					continue;
				}

				_states[state.Name] = state;
				_signals.Connect(
					() => state.Transitioned += OnChildTransitioned,
					() => state.Transitioned -= OnChildTransitioned);
			}

			_isReady = true;

			var initialStateName = NormalizeStateName(_pendingStateName);
			if (string.IsNullOrEmpty(initialStateName))
			{
				initialStateName = CurrentState?.Name ?? string.Empty;
			}

			if (!_states.TryGetValue(initialStateName, out var initialState))
			{
				Logger.Error($"Missing initial state '{initialStateName}' on {GetPath()}.");
				return;
			}

			CurrentState = initialState;
			_pendingStateName = initialState.Name;
			CurrentState.Enter(new Dictionary());
		}

		/// <summary>
		/// Forwards per-frame updates to the active child state.
		/// Callers use this as the non-physics entry point so only the authoritative current state receives runtime work.
		/// </summary>
		public void Update(double delta)
		{
			CurrentState?.Update(delta);
		}

		/// <summary>
		/// Forwards physics updates to the active child state.
		/// This keeps dead/alive or locomotion-specific physics behavior behind the shared state-machine contract.
		/// </summary>
		public void PhysicsUpdate(double delta)
		{
			CurrentState?.PhysicsUpdate(delta);
		}

		/// <summary>
		/// Transitions the machine to another authored child state.
		/// Systems call this when gameplay authority decides the next state, and the machine handles exit, enter, signal emission,
		/// and replicated state-name updates from one generic location.
		/// </summary>
		public void TransitionState(string newStateName, Dictionary args = null)
		{
			SetCurrentState(newStateName, args ?? new Dictionary(), deferUntilReady: false);
		}

		private void OnChildTransitioned(string newStateName, Dictionary args = null)
		{
			SetCurrentState(newStateName, args ?? new Dictionary(), deferUntilReady: false);
		}

		private void SetCurrentState(string newStateName, Dictionary args, bool deferUntilReady)
		{
			var normalizedStateName = NormalizeStateName(newStateName);
			if (string.IsNullOrEmpty(normalizedStateName))
			{
				return;
			}

			if (!_isReady)
			{
				if (deferUntilReady)
				{
					_pendingStateName = normalizedStateName;
				}

				return;
			}

			if (!_states.TryGetValue(normalizedStateName, out var nextState))
			{
				Logger.Warning($"Missing state '{normalizedStateName}' on {GetPath()}.");
				return;
			}

			if (CurrentState == nextState)
			{
				_pendingStateName = nextState.Name;
				return;
			}

			var previousStateName = CurrentState?.Name ?? string.Empty;
			CurrentState?.Exit();
			CurrentState = nextState;
			_pendingStateName = nextState.Name;
			CurrentState.Enter(args);
			EmitSignal(nameof(StateChanged), previousStateName, CurrentState.Name);
		}

		private static string NormalizeStateName(string stateName)
		{
			return string.IsNullOrWhiteSpace(stateName) ? string.Empty : stateName;
		}
	}
}
