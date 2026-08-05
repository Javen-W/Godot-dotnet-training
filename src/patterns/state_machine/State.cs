using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace GodotDotnetTraining
{
	[GlobalClass]
	public partial class State : Node
	{
		[Signal]
		public delegate void TransitionedEventHandler(string new_state_name, Dictionary args);

		public virtual void Enter(Dictionary args) { }

		public virtual void Exit() { }

		public virtual void Update(double _delta) { }

		public virtual void PhysicsUpdate(double _delta) { }
	}
}
