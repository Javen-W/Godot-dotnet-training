using Godot;

namespace GodotDotnetTraining
{
	/// <summary>
	/// Base blueprint for all items. Carries immutable shared metadata and authored assets
	/// that can be referenced by many runtime <see cref="ItemData"/> instances.
	///
	/// Intentionally NOT marked <c>[DataContract]</c>: <see cref="Icon"/> is a
	/// <see cref="Texture2D"/> Godot resource reference that cannot be round-tripped
	/// through JSON.  Derived classes that use <c>[DataContract]</c> opt-in to
	/// serialise only their own <c>[DataMember]</c> properties, leaving
	/// <see cref="Icon"/> (and any future assets added here) out of the JSON payload.
	///
	/// Mutable properties live on the serializable instances, not on the blueprint.
	/// <see cref="ItemData"/> 
	/// </summary>
	[GlobalClass]
	public partial class ItemDef : Resource
	{
		/// <summary>Unique identifier used to register and look up this item in ResourceRegistry.</summary>
		[Export]
		public ItemID ItemID { get; set; }

		[Export]
		public string Name { get; set; }

		/// <summary>2D icon shown in inventory and equipment UI.</summary>
		[Export]
		public Texture2D Icon { get; set; }

		/// <summary>3D scene attached to the player's skeleton when this item is equipped.</summary>
		[Export]
		public PackedScene EquippedModel { get; set; }

		/// <summary>3D/2D scene spawned in the world when this item is dropped on the ground.</summary>
		[Export]
		public PackedScene DropModel { get; set; }

		#region Constructors

		// Make sure you provide a parameterless constructor.
		public ItemDef() : this(name: "") { }

		public ItemDef(string name)
		{
			Name = name;
		}

		#endregion
	}
}
