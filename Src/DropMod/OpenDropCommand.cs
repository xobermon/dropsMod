using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DropMod
{
    // commands are multi-step processes that do something, typically associated with a job
    public class OpenDropCommand : Command
    {
        public DropActor target { get; private set; }

        public OpenDropCommand(DropActor target, Actor owner) : base(owner)
        {
            this.target = target;
        }

        public OpenDropCommand(OctSaveInitializer initializer) : base(initializer)
        { }

        public override string ToString()
        {
            if (target != null)
            {
                return "Looting " + target.type.name;
            }
            else
            {
                return "Looting";
            }
        }

        public override bool CanExecute(CommandCharacterFacet forExecutor, OctScriptContext castingContext = null, Command parent = null, bool micro = false)
        {
            if (!base.CanExecute(forExecutor, castingContext, parent, micro))
            {
                return false;
            }

            // must be reachable to be able to execute
            if (!target.CanPathTo(forExecutor.actor))
            {
                BlockedFor(forExecutor, COMMAND_BLOCKED_UNREACHABLE);
                return false;
            }

            return true;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);

            // done? Complete!
            if (target == null || target.destroyed)
            {
                Complete();
                return;
            }

            // boiler plate flow handling
            if (bodyVerb != null)
            {
                // done with previous verb?
                if (bodyVerb.complete)
                {
                    // going to? move on to next?
                    if (bodyVerb is GotoVerb)
                    {
                        bodyVerb = null;
                    }
                    // open? we done!
                    else if (bodyVerb is OpenDropVerb)
                    {
                        Complete();
                        return;
                    }
                }
                // if interrupted, block for now and pick up later
                else if (bodyVerb.interrupted)
                {
                    Blocked();
                    return;
                }
                // if failed, block, if failed to path - block longer
                else if (bodyVerb.failed)
                {
                    Blocked(bodyVerb is GotoVerb ? Command.COMMAND_BLOCKED_PATH_FAILED : Command.COMMAND_BLOCKED_SOFT);
                    return;
                }
            }

            // wait for previous verb to finish
            if (bodyVerb == null)
            {
                // are we close enough?
                if (target.GetPathingTargetDistance(executor.actor) <= .5f)
                {
                    // open it
                    AttemptTransitionToVerb(new OpenDropVerb(target));
                }
                else
                {
                    // path to it (roll our hauling skill for movement speed)
                    AttemptTransitionToVerb(new GotoVerb(target, target.pos, true, "Hauling", .5f));
                }
            }
        }
    }
}
