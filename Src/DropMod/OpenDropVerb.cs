using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DropMod
{
    // verbs are chunks of game code that run alongside an animation
    class OpenDropVerb : Verb
    {
        public DropActor target { get; private set; }
        public TimeStamp breakAfter { get; private set; }

        public OpenDropVerb(DropActor target) : base()
        {
            this.target = target;
        }

        public OpenDropVerb(OctSaveInitializer initializer) : base(initializer)
        { }

        protected override void Assigned(Verb previousVerb)
        {
            base.Assigned(previousVerb);

            // put all weaps away
            executor.character.inventory.StowAll();

            // look at it
            executor.character.SetForward((target.pos - executor.actor.pos).normalized);

            // play the animation (it'll auto transition back to idle when the verb is finished)
            executor.character.animations.PlayAnimation("Armature|Pick");

            // take 4 times as long as a normal incremental action
            breakAfter = RollRate("Hauling", 4);
        }

        public override void Tick(float dt)
        {
            // did we lose our target?
            if (target == null)
            {
                Failed();
                return;
            }
            else if (target.destroyed)
            {
                Failed();
                return;
            }

            base.Tick(dt);

            // time to try to break?
            if (TimeManager.Instance.seconds > breakAfter)
            {
                // success?
                if (executor.character.SuccessRoll("Hauling"))
                {
                    // success xp! (*10 because 10x as slow as normal actions)
                    executor.character.AwardExperience(Character.AttributeSuccessXP * 4, "Hauling");

                    // break it
                    target.BreakOpen();

                    // breaking it open should have marked us complete, but make sure
                    if (executor != null)
                    {
                        // mark ourselves complete because we're done
                        Complete();
                    }
                    
                    return;
                }
                else
                {
                    // failed, learn from our failure
                    executor.character.AwardExperience(Character.AttributeFailureXP * 4, "Hauling");

                    // roll next attempt and update our anims peed
                    breakAfter = RollRate("Hauling", 4);
                }
            }
        }
    }
}
