#region Copyright & License Information
/*
 * Modded by Boolbada of OP Mod, from EngineerRepair logic.
 * 
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

//using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.yupgi_alert.Activities;
using OpenRA.Mods.Common.Orders;
//using OpenRA.Primitives;
using System.Drawing;
using OpenRA.Traits;

namespace OpenRA.Mods.yupgi_alert.Traits
{
	[Desc("This player actor has this many Nydus canals. Must be attached to the player by attaching in player.yaml, otherwise Nydus logic won't work.")]
	public class NydusCounterInfo : TraitInfo<NydusCounter> { }
	public class NydusCounter
	{
		public int cnt = 0;
		public Actor PrimaryActor = null;
	}

	[Desc("This actor can teleport actors like Nydus canels in SC1. Assuming static object. The actor must have PrimaryBuilding trait so that exit can be specified!")]
	public class NydusInfo : ITraitInfo
	{
		public object Create(ActorInitializer init) { return new Nydus(init, this); }
	}

	// The nydus canal does nothing.
	// The actor teleports itself, upon entering: works the same as EngineerRepairalbe trait.
	public class Nydus: INotifyCreated, INotifyActorDisposing, INotifyOwnerChanged
	{
		public Nydus(ActorInitializer init, NydusInfo info)
		{
		}

		void IncreaseNydusCnt(Actor self, Player owner)
		{
			var counter = owner.PlayerActor.Trait<NydusCounter>();
			// Assign itself as primary, when first one.
			if (counter.cnt == 0)
			{
				var pri = self.Trait<NydusPrimaryExit>();
				pri.SetPrimary(self);
			}
			counter.cnt++;
			Game.Debug(owner + " now has nydus cnt " + counter.cnt);
		}

		void DecreaseNydusCnt(Actor self, Player owner)
		{
			var counter = owner.PlayerActor.Trait<NydusCounter>();
			counter.cnt--;
			Game.Debug(owner + " now has nydus cnt " + counter.cnt);

			// what if primary was killed?
			if (self.IsPrimaryNydusExit())
			{
				Game.Debug("was primary");
				var actors = self.World.ActorsWithTrait<NydusPrimaryExit>()
					.Where(a => a.Actor.Owner == self.Owner && a.Actor != self);
				if (!actors.Any())
				{
					// no nydus canal left.
					counter.PrimaryActor = null;
				}
				else
				{
					var pri = actors.First().Actor;
					pri.Trait<NydusPrimaryExit>().SetPrimary(pri);
				}
			}
		}

		public void Created(Actor self) { IncreaseNydusCnt(self, self.Owner); }
		public void Disposing(Actor self) { DecreaseNydusCnt(self, self.Owner); }
		//public void AddedToWorld(Actor self) { IncreaseNydusCnt(self.Owner); } // created happens first. no need.
		//public void Killed(Actor self, AttackInfo e) { DecreaseNydusCnt(self.Owner); } // killed then disposed. no need.
		//public void Sold(Actor self) { DecreaseNydusCnt(self.Owner); } // sold and disposed. no need.
		//public void Selling(Actor self) { }

		public void OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			// this one is trickier haha.
			IncreaseNydusCnt(self, newOwner);
			DecreaseNydusCnt(self, oldOwner); // probably oldOwner == self.Owner but to avoid risk...
		}
	}

	[Desc("Can move instantly to primary designated nydus canal actors.")]
	class NydusTransportableInfo : ITraitInfo
	{
		[VoiceReference]
		public readonly string Voice = "Move";
		public readonly string EnterCursor = "enter";
		public readonly string EnterBlockedCursor = "enter-blocked";
		public object Create(ActorInitializer init) { return new NydusTransportable(init, this); }
	}

	class NydusTransportable : IIssueOrder, IResolveOrder, IOrderVoice
	{
		readonly NydusTransportableInfo info;

		public NydusTransportable(ActorInitializer init, NydusTransportableInfo info)
		{
			this.info = info;
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get { yield return new NydusTransportOrderTargeter(info); }
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, Target target, bool queued)
		{
			if (order.OrderID != "NydusTransport")
				return null;

			if (target.Type == TargetType.FrozenActor)
				//return new Order(order.OrderID, self, queued) { ExtraData = target.FrozenActor.ID };
				return null;

			return new Order(order.OrderID, self, queued) { TargetActor = target.Actor };
		}

		// Does the player own enough nydus canals?
		// For nydus canals to work, the player needs at least 2.
		// Actually, at least 1 but in that case, the unit goes in and immediately comes out. That's stupid.
		static bool HasEnoughCanals(Actor self)
		{
			var counter = self.Owner.PlayerActor.Trait<NydusCounter>();
			return counter.cnt > 1;
		}

		static bool IsValidOrder(Actor self, Order order)
		{
			// Not targeting a frozen actor
			if (order.ExtraData == 0 && order.TargetActor == null)
				return false;

			// The owner must own at least two nydus canals.
			if (!HasEnoughCanals(self))
				return false;

			// primary buildings are where entered units exit.
			// You don't want to put them in the primary building.
			return !order.TargetActor.IsPrimaryNydusExit();
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == "NydusTransport" && IsValidOrder(self, order)
				? info.Voice : null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != "NydusTransport" || !IsValidOrder(self, order))
				return;

			var target = self.ResolveFrozenActorOrder(order, Color.Yellow);
			if (target.Type != TargetType.Actor)
				return;

			if (!order.Queued)
				self.CancelActivity();

			self.SetTargetLine(target, Color.Yellow);
			self.QueueActivity(new EnterNydus(self, target.Actor, EnterBehaviour.Exit));
		}

		class NydusTransportOrderTargeter : UnitOrderTargeter
		{
			NydusTransportableInfo info;

			public NydusTransportOrderTargeter(NydusTransportableInfo info)
				: base("NydusTransport", 6, info.EnterCursor, false, true)
			{
				this.info = info;
			}

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!target.Info.HasTraitInfo<NydusInfo>())
					return false;

				if (self.Owner != target.Owner)
					// can only enter player owned one.
					return false;

				if (target.IsPrimaryNydusExit()) // block, if primary exit.
					cursor = info.EnterBlockedCursor;

				return true;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				// You can't enter frozen actor.
				return false;
			}
		}
	}
}
