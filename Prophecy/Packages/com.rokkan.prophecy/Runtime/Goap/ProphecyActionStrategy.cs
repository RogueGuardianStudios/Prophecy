using RGS.GOAP.Core;
using RGS.GOAP.Core.Interfaces;
using RGS.GOAP.Core.Strategies;
using Rokkan.Prophecy.Presentation;

namespace Rokkan.Prophecy.Goap
{
    /// <summary>
    /// Shared plumbing for every Prophecy action: reach the body, and write intent.
    ///
    /// <para><b>The one rule these all obey.</b> A strategy writes <see cref="EnemyIntent"/> and
    /// nothing else. It never sets a position, never moves a transform, never calls an ability. The
    /// intent becomes an <c>InputFrame</c> and the simulation decides what that means — so a
    /// planned attack is subject to the same action lock, cancel window, cover check and i-frames a
    /// player's attack is.</para>
    ///
    /// <para>This is the line that erodes without a compiler to defend it. A strategy that moved
    /// the transform directly would look correct on screen and be outside every combat rule, with
    /// nothing failing to say so. If you are writing a new action and reaching for anything other
    /// than intent, that is the moment to stop.</para>
    /// </summary>
    public abstract class ProphecyActionStrategy : BaseGoapActionStrategy
    {
        /// <summary>The body's brain host, or null if this agent is not a Prophecy character.</summary>
        protected static EnemyBrainHost HostOf(IGoapAgentContext context)
        {
            var cached = context?.GetCapability<EnemyBrainHost>();
            if (cached != null) return cached;

            // Fall back to a lookup: an agent assembled at runtime may not have pre-cached.
            var go = context?.gameObject;
            return go != null && go.TryGetComponent(out EnemyBrainHost host) ? host : null;
        }

        /// <summary>
        /// Announce that a planner is driving, so the host's built-in loop stands down. Called on
        /// start rather than once at wake-up because a brain can be swapped or disabled at runtime,
        /// and an enemy whose planner stopped should fall back to patrolling rather than freeze.
        /// </summary>
        protected EnemyBrainHost TakeOver(IGoapAgentContext context)
        {
            var host = HostOf(context);

            // Instance rather than static so the asset's name can go with it: the host's trace has
            // no other way to name the planner's current action, and the assembly dependency only
            // runs this way — Prophecy's runtime does not reference GOAP.
            host?.DriveExternally(true, name);
            return host;
        }
    }
}
