using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DropMod
{
    public class DropActor : Actor
    {
        // helper to get our specific flavor of behavior
        public DropBehavior dropBeh
        {
            get
            {
                return behavior as DropBehavior;
            }
        }

        // snap to surface position
        public override bool SnapToSurface
        {
            get
            {
                return true;
            }
        }

        public Notification notification { get; private set; }

        public DropType type { get; private set; }

        public TimeStamp droppedAt { get; private set; }

        // issues loot command
        public OpenDropCommand lootCommand { get; private set; }
        public Command GetOrIssueLootCommand()
        {
            if (lootCommand == null)
            {
                lootCommand = new OpenDropCommand(this, FactionManager.Instance.playerFaction);
                lootCommand.onDestroy += ClearOpenCommand;
            }
            return lootCommand;
        }
        private void ClearOpenCommand(Command command)
        {
            lootCommand = null;
        }

        public DropActor(DropType type, TilePos tile) : base(tile)
        {
            this.type = type;

            droppedAt = TimeManager.Instance.seconds;

            // register it
            DropManager.Instance.AddDrop(this);
        }

        public DropActor(OctSaveInitializer initializer)
        { }

        protected override void Destroyed()
        {
            // deregister us
            DropManager.Instance.RemoveDrop(this);

            // kill our notification
            if (notification != null)
            {
                notification.Destroy();
                notification = null;
            }

            // kill the loot command 
            if (lootCommand != null)
            {
                lootCommand.Cancel();
            }

            // handle our stuff before we call up the chain
            base.Destroyed();
        }

        public void BreakOpen()
        {
            // up to 50% of ruler's prestige, but exponential from 0
            float worth = Mathf.Max(KingdomManager.Instance.playerKingdom.ruler.GetPrestige() * Mathf.Pow(Random.value, 2f) * OctoberMath.DistributedRandom(0f, .5f), 5);
            float balance = worth;
            while (balance >= 1)
            {
                IItemType chosenType = null;
                float chosenValue = float.MaxValue;
                float totalWeight = 0;
                float value = Mathf.Min(balance, 1 + (worth * OctoberMath.DistributedRandom(0f, 1f, 1.5f) * Mathf.Pow(Random.value, 2)));
                for (int i = ItemManager.Instance.types.Count - 1; i >= 0; --i)
                {
                    IItemType itemType = ItemManager.Instance.types[i];

                    // can filter to specific items
                    if (type.filter != null)
                    {
                        if (!type.filter.Includes(itemType))
                        {
                            continue;
                        }
                    }

                    // instanced?
                    if (itemType is InstancedItemType potInstType)
                    {
                        // don't drop if it's for an exclusive class
                        if (potInstType.exclusive != null)
                        {
                            continue;
                        }

                        // has value?
                        System.Nullable<InstancedItemStat> stat = potInstType.GetStat("Value");
                        if (stat != null)
                        {
                            if (stat.Value.min <= value && stat.Value.max >= value)
                            {
                                float potValue = Mathf.Min(value, stat.Value.max);
                                float weight = OctoberMath.DistributedRandom(.5f, 1.5f) * potInstType.carryChance;
                                if (weight > 0)
                                {
                                    totalWeight += weight;
                                    if (Random.value * totalWeight <= weight)
                                    {
                                        chosenType = itemType;
                                        chosenValue = potValue;
                                    }
                                }
                            }
                        }
                    }
                    // stacked?
                    else if (itemType is StackedItemType potStackType)
                    {
                        float perc = (value / potStackType.value);
                        if (perc >= 0)
                        {
                            float weight = OctoberMath.DistributedRandom(.5f, 1.5f) * potStackType.carryChance;
                            totalWeight += weight;
                            if (Random.value * totalWeight <= weight)
                            {
                                chosenType = itemType;
                                chosenValue = Mathf.Min(value, potStackType.value);
                            }
                        }
                    }
                }

                // didn't find anything
                if (chosenType == null)
                {
                    break;
                }

                // if it isn't concrete, resolve a concrete type in this world
                if (!chosenType.IsConcrete())
                {
                    if (chosenType is ItemType chosenItemType)
                    {
                        chosenType = chosenItemType.ResolveType();
                    }
                }

                if (chosenType.definition is InstancedItemType instType)
                {
                    // turns the desired value back into quality and durability
                    instType.ValueToQuality(chosenValue, out float quality, out float healthNorm);
                    InstancedItem item = instType.NewItem(chosenType, new InstancedItemInitializer(quality, healthNorm, null));

                    // drops an item from this position
                    ItemManager.Instance.DropItem(pos, item, Ignored.No, Log.Added, "looted");
                }
                else if (chosenType.definition is StackedItemType stackType)
                {
                    int count = OctoberMath.FloorToInt((chosenValue / stackType.value) * stackType.stackLimit);
                    if (count > 0)
                    {
                        ItemManager.Instance.DropStack(pos, chosenType, count, 1, Ignored.No, Log.Added, "looted");
                    }
                }
                balance -= chosenValue;
            }

            // destroy ourselves when we're done
            Destroy();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);

            // discovered?
            if (notification == null)
            {
                // any players close to us?
                if (FactionManager.Instance.playerFaction is Faction playerFaction)
                {
                    foreach (var member in playerFaction.characters)
                    {
                        // only mortals
                        if (member.type.genus != CharacterType.Genus.Humanoid)
                        {
                            continue;
                        }

                        Vector3 diff = member.pos - pos;
                        // close enough
                        if (diff.magnitude < 5f)
                        {
                            // can they see it?
                            if (member.perception.GetLineOfSight(this))
                            {
                                // how long until it decays
                                TimeStamp fades = droppedAt + TimeManager.SecondsPerDay * DropManager.Instance.dropDecayDays;

                                // show it
                                float remaining = (float)(fades - TimeManager.Instance.seconds);

                                // discover it
                                notification = new Notification("Dropped Supplies discovered!", this, remaining);
                            }
                        }
                    }
                }
            }
            

            // time to decay?
            if (TimeManager.Instance.seconds > droppedAt + TimeManager.SecondsPerDay * DropManager.Instance.dropDecayDays)
            {
                Destroy();
                return;
            }
        }

        public override string GetName()
        {
            return type.name;
        }

        public override void VisitProperties(IActorPropertyVisitor visitor)
        {
            base.VisitProperties(visitor);

            // how long until it decays!
            visitor.VisitSingleTextProperty("Decays", TimeManager.Instance.TimeUntil(droppedAt + TimeManager.SecondsPerDay * DropManager.Instance.dropDecayDays));

            // show the Loot command button and icon button and hotkey!
            visitor.VisitSharedCommandProperty("Loot", GetOrIssueLootCommand, lootCommand, true, CommandManager.Instance.GetCommandType("Loot Drop"), 0, true, "Primary");
        }

        public override void VisitThirdPersonInteractions(Character forCharacter, System.Action<IThirdPersonInteraction> visitor)
        {
            base.VisitThirdPersonInteractions(forCharacter, visitor);

            // third person interactions don't use commands - they go straight to the verbs
            visitor(new VerbThirdPersonInteraction(ThirdPersonInteractionType.Interact, this, "Loot", delegate { return new OpenDropVerb(this); }, 100));
        }

        // no multiselect of these
        public override bool CanAddToSelection(Actor selected)
        {
            return false;
        }
    }
}
