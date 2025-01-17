#region Copyright & License Information
/**
 * Copyright (c) The OpenRA Combined Arms Developers (see CREDITS).
 * This file is part of OpenRA Combined Arms, which is free software.
 * It is made available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License,
 * or (at your option) any later version. For more information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.CA.Activities;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.CA.Traits
{
	[Desc("CA version makes trait conditional and allows for a condition applied upon teleporting, and allows use of ammo pool for multiple charges.")]
	class PortableChronoCAInfo : PausableConditionalTraitInfo, Requires<IMoveInfo>
	{
		[Desc("Cooldown in ticks until the unit can teleport.")]
		public readonly int ChargeDelay = 500;

		[Desc("Can the unit teleport only a certain distance?")]
		public readonly bool HasDistanceLimit = true;

		[Desc("The maximum distance in cells this unit can teleport (only used if HasDistanceLimit = true).")]
		public readonly int MaxDistance = 12;

		[Desc("Sound to play when teleporting.")]
		public readonly string ChronoshiftSound = "chrotnk1.aud";

		[CursorReference]
		[Desc("Cursor to display when able to deploy the actor.")]
		public readonly string DeployCursor = "deploy";

		[CursorReference]
		[Desc("Cursor to display when unable to deploy the actor.")]
		public readonly string DeployBlockedCursor = "deploy-blocked";

		[CursorReference]
		[Desc("Cursor to display when targeting a teleport location.")]
		public readonly string TargetCursor = "chrono-target";

		[CursorReference]
		[Desc("Cursor to display when the targeted location is blocked.")]
		public readonly string TargetBlockedCursor = "move-blocked";

		[Desc("Kill cargo on teleporting.")]
		public readonly bool KillCargo = true;

		[Desc("Flash the screen on teleporting.")]
		public readonly bool FlashScreen = false;

		[GrantedConditionReference]
		[Desc("The condition to grant after teleporting.")]
		public readonly string TeleportCondition = null;

		[Desc("How long to apply the condition for.")]
		public readonly int ConditionDuration = 0;

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Range circle color.")]
		public readonly Color CircleColor = Color.FromArgb(128, Color.LawnGreen);

		[Desc("Range circle line width.")]
		public readonly float CircleWidth = 1;

		[Desc("Range circle border color.")]
		public readonly Color CircleBorderColor = Color.FromArgb(96, Color.Black);

		[Desc("Range circle border width.")]
		public readonly float CircleBorderWidth = 3;

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.LawnGreen;

		[Desc("Number of charges.")]
		public readonly int Charges = 1;

		[Desc("If true, gain max charges after recharging.")]
		public readonly bool RechargeToMax = false;

		[Desc("If true recharge will reset any ongoing recharge on teleport.")]
		public readonly bool ResetRechargeOnUse = true;

		[Desc("Cooldown between jumps (irrespective of charge).")]
		public readonly int Cooldown = 0;

		public readonly bool ShowSelectionBar = true;
		public readonly bool ShowSelectionBarWhenFull = true;

		[Desc("Selection bar color.")]
		public readonly Color SelectionBarColor = Color.Magenta;

		public readonly bool ShowCooldownSelectionBar = false;

		[Desc("Cooldown selection bar color.")]
		public readonly Color CooldownSelectionBarColor = Color.Silver;

		public readonly bool RequireEmptyDestination = false;

		public override object Create(ActorInitializer init) { return new PortableChronoCA(init.Self, this); }
	}

	class PortableChronoCA : PausableConditionalTrait<PortableChronoCAInfo>, IIssueOrder, IResolveOrder, ITick, ISelectionBar, IOrderVoice, ISync, IIssueDeployOrder
	{
		public readonly new PortableChronoCAInfo Info;
		readonly IMove move;

		[Sync]
		int chargeTick = 0;

		[Sync]
		int conditionTicks = 0;
		int cooldownTicks = 0;

		int token = Actor.InvalidConditionToken;
		IPortableChronoModifier[] modifiers;

		public int ChargeDelay { get; private set; }
		public int MaxDistance { get; private set; }
		public int MaxCharges { get; private set; }
		public int Charges { get; private set; }

		public PortableChronoCA(Actor self, PortableChronoCAInfo info)
			: base(info)
		{
			Info = info;
			move = self.Trait<IMove>();
			ChargeDelay = Info.ChargeDelay;
			MaxDistance = Info.MaxDistance;
			MaxCharges = Info.Charges;
			Charges = Info.Charges;
		}

		protected override void Created(Actor self)
		{
			modifiers = self.TraitsImplementing<IPortableChronoModifier>().ToArray();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return;

			if (cooldownTicks > 0)
				cooldownTicks--;

			if (chargeTick > 0)
			{
				chargeTick--;

				if (chargeTick == 0)
				{
					if (Info.RechargeToMax)
					{
						Charges = MaxCharges;
					}
					else
					{
						if (Charges < MaxCharges)
							Charges++;

						if (Charges < MaxCharges)
							ResetChargeTime();
					}
				}
			}

			if (--conditionTicks < 0 && token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			// HACK: Switch the global order generator instead of actually issuing an order
			if (CanTeleport)
				self.World.OrderGenerator = new PortableChronoOrderGenerator(self, this);

			// HACK: We need to issue a fake order to stop the game complaining about the bodge above
			return new Order("PortableChronoDeploy", self, Target.Invalid, queued);
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) { return !IsTraitPaused && !IsTraitDisabled; }

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				if (IsTraitDisabled)
					yield break;

				yield return new PortableChronoOrderTargeter(Info.TargetCursor);
				yield return new DeployOrderTargeter("PortableChronoDeploy", 5,
					() => CanTeleport ? Info.DeployCursor : Info.DeployBlockedCursor);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "PortableChronoDeploy")
			{
				// HACK: Switch the global order generator instead of actually issuing an order
				if (CanTeleport)
					self.World.OrderGenerator = new PortableChronoOrderGenerator(self, this);

				// HACK: We need to issue a fake order to stop the game complaining about the bodge above
				return new Order(order.OrderID, self, Target.Invalid, queued);
			}

			if (order.OrderID == "PortableChronoTeleport")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "PortableChronoTeleport" && CanTeleport && order.Target.Type != TargetType.Invalid)
			{
				var maxDistance = Info.HasDistanceLimit ? MaxDistance : (int?)null;
				if (!order.Queued)
					self.CancelActivity();

				var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
				if (maxDistance != null)
					self.QueueActivity(move.MoveWithinRange(order.Target, WDist.FromCells(maxDistance.Value), targetLineColor: Info.TargetLineColor));

				self.QueueActivity(new TeleportCA(self, cell, maxDistance, Info.KillCargo, Info.FlashScreen, Info.ChronoshiftSound, true, false, default(BitSet<DamageType>), Info.RequireEmptyDestination));
				self.QueueActivity(move.MoveTo(cell, 5, targetLineColor: Info.TargetLineColor));
				self.ShowTargetLines();
			}
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == "PortableChronoTeleport" && CanTeleport ? Info.Voice : null;
		}

		public void GrantCondition(Actor self)
		{
			if (string.IsNullOrEmpty(Info.TeleportCondition))
				return;

			if (token == Actor.InvalidConditionToken)
			{
				token = self.GrantCondition(Info.TeleportCondition);
				conditionTicks = Info.ConditionDuration;
			}
		}

		public void ConsumeCharge()
		{
			cooldownTicks = Info.Cooldown;

			Charges--;

			if (Info.ResetRechargeOnUse || chargeTick == 0)
				ResetChargeTime();
		}

		public void ResetChargeTime()
		{
			chargeTick = ChargeDelay;
		}

		public void UpdateModifiers()
		{
			MaxDistance = OpenRA.Mods.Common.Util.ApplyPercentageModifiers(Info.MaxDistance, modifiers.Select(m => m.GetRangeModifier()));
			var chargePerc = Info.ChargeDelay > 0 ? (float)chargeTick / ChargeDelay : 1;
			ChargeDelay = OpenRA.Mods.Common.Util.ApplyPercentageModifiers(Info.ChargeDelay, modifiers.Select(m => m.GetCooldownModifier()));
			chargeTick = chargeTick > 0 ? (int)(ChargeDelay * chargePerc) : 0;
			var prevMaxCharges = MaxCharges;
			MaxCharges = Info.Charges + modifiers.Sum(m => m.GetExtraCharges());

			if (prevMaxCharges < MaxCharges)
				Charges += MaxCharges - prevMaxCharges;

			if (Charges < MaxCharges && chargeTick == 0)
				ResetChargeTime();
			else if (Charges > MaxCharges)
				Charges = MaxCharges;
		}

		public bool CanTeleport => !IsTraitDisabled && !IsTraitPaused && Charges > 0 && cooldownTicks == 0;

		float ISelectionBar.GetValue()
		{
			if (IsTraitDisabled)
				return 0f;

			if (Info.ShowCooldownSelectionBar && cooldownTicks > 0 && Charges > 0)
				return (float)(Info.Cooldown - cooldownTicks) / Info.Cooldown;

			if (!Info.ShowSelectionBar || chargeTick == ChargeDelay)
				return 0f;

			if (!Info.ShowSelectionBarWhenFull && chargeTick == 0)
				return 0f;

			return (float)(ChargeDelay - chargeTick) / ChargeDelay;
		}

		Color ISelectionBar.GetColor() { return Info.ShowCooldownSelectionBar && cooldownTicks > 0 && Charges > 0 ? Info.CooldownSelectionBarColor : Info.SelectionBarColor; }
		bool ISelectionBar.DisplayWhenEmpty => false;

		protected override void TraitDisabled(Actor self)
		{
			chargeTick = 0;

			if (token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);
		}
	}

	class PortableChronoOrderTargeter : IOrderTargeter
	{
		readonly string targetCursor;

		public PortableChronoOrderTargeter(string targetCursor)
		{
			this.targetCursor = targetCursor;
		}

		public string OrderID => "PortableChronoTeleport";
		public int OrderPriority => 5;
		public bool IsQueued { get; protected set; }
		public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }

		public bool CanTarget(Actor self, in Target target, ref TargetModifiers modifiers, ref string cursor)
		{
			if (modifiers.HasModifier(TargetModifiers.ForceMove))
			{
				var xy = self.World.Map.CellContaining(target.CenterPosition);

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				if (self.IsInWorld && self.Owner.Shroud.IsExplored(xy))
				{
					cursor = targetCursor;
					return true;
				}

				return false;
			}

			return false;
		}
	}

	class PortableChronoOrderGenerator : OrderGenerator
	{
		readonly Actor self;
		readonly PortableChronoCA portableChrono;
		readonly PortableChronoCAInfo info;
		readonly IEnumerable<TraitPair<PortableChronoCA>> selectedWithAbility;

		public PortableChronoOrderGenerator(Actor self, PortableChronoCA portableChrono)
		{
			this.self = self;
			this.portableChrono = portableChrono;
			info = portableChrono.Info;

			selectedWithAbility = self.World.Selection.Actors
				.Where(a => a.Info.HasTraitInfo<PortableChronoCAInfo>() && a != self && a.Owner == self.Owner && !a.IsDead)
				.Select(a => new TraitPair<PortableChronoCA>(a, a.Trait<PortableChronoCA>()));
		}

		protected override IEnumerable<Order> OrderInner(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (mi.Button == MouseButton.Right)
			{
				world.CancelInputMode();
				yield break;
			}

			if (self.IsInWorld && self.Location != cell
				&& self.Trait<PortableChronoCA>().CanTeleport && self.Owner.Shroud.IsExplored(cell))
			{
				world.CancelInputMode();
				var targetCell = Target.FromCell(world, cell);
				yield return new Order("PortableChronoTeleport", self, targetCell, mi.Modifiers.HasModifier(Modifiers.Shift));

				foreach (var other in selectedWithAbility)
				{
					if (other.Actor.IsInWorld && other.Trait.CanTeleport && other.Actor.Owner == self.Owner)
					{
						yield return new Order("PortableChronoTeleport", other.Actor, targetCell, mi.Modifiers.HasModifier(Modifiers.Shift));
					}
				}
			}
		}

		protected override void SelectionChanged(World world, IEnumerable<Actor> selected)
		{
			if (!selected.Contains(self))
				world.CancelInputMode();
		}

		protected override void Tick(World world)
		{
			if (portableChrono.IsTraitDisabled || portableChrono.IsTraitPaused)
			{
				world.CancelInputMode();
				return;
			}
		}

		protected override IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }

		protected override IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }

		protected override IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world)
		{
			if (!self.IsInWorld || self.Owner != self.World.LocalPlayer)
				yield break;

			if (!info.HasDistanceLimit)
				yield break;

			yield return new RangeCircleAnnotationRenderable(
				self.CenterPosition,
				WDist.FromCells(info.MaxDistance),
				0,
				info.CircleColor,
				info.CircleWidth,
				info.CircleBorderColor,
				info.CircleBorderWidth);

			foreach (var other in selectedWithAbility)
			{
				if (other.Actor.IsInWorld && other.Trait.Info.HasDistanceLimit && other.Trait.CanTeleport && self.Owner == self.World.LocalPlayer)
				{
					yield return new RangeCircleAnnotationRenderable(
						other.Actor.CenterPosition + new WVec(0, other.Actor.CenterPosition.Z, 0),
						WDist.FromCells(other.Trait.Info.MaxDistance),
						0,
						other.Trait.Info.CircleColor,
						other.Trait.Info.CircleWidth,
						other.Trait.Info.CircleBorderColor,
						other.Trait.Info.CircleBorderWidth);
				}
			}
		}

		protected override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			if (self.IsInWorld && self.Location != cell
				&& portableChrono.CanTeleport && self.Owner.Shroud.IsExplored(cell))
				return info.TargetCursor;
			else
				return info.TargetBlockedCursor;
		}
	}
}
