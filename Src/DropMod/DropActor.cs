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

            void DropContents(IItemType type, float value)
            {
                if (type.definition is InstancedItemType instType)
                {
                    // turns the desired value back into quality and durability
                    instType.ValueToQuality(value, out float quality, out float healthNorm);
                    InstancedItem item = instType.NewItem(type, new InstancedItemInitializer(quality, healthNorm, null));

                    // drops an item from this position
                    ItemManager.Instance.DropItem(pos, item, Ignored.No, Log.Added, "looted");
                }
                else if (type.definition is StackedItemType stackType)
                {
                    int count = OctoberMath.FloorToInt(value / stackType.ValuePer());
                    if (count > 0)
                    {
                        ItemManager.Instance.DropStack(pos, type, count, 1, Ignored.No, Log.Added, "looted");
                    }
                }
            }
            ItemManager.Instance.GenerateLoot(worth, DropContents, type.filter);

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

                        // only people on the same map
                        if (!member.OnMap(map))
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
                                notification = new Notification(type.name + " discovered!", this, remaining);
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
