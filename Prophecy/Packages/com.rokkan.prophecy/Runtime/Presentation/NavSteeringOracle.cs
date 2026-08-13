using UnityEngine;
using UnityEngine.AI;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The agreed NavMesh shape (Matt, 2026-08-04): <b>the agent is a steering oracle, never a
    /// mover.</b> A body that carries this component gains a hidden <see cref="NavMeshAgent"/>
    /// with <c>updatePosition</c>/<c>updateRotation</c> off; a GOAP strategy hands its desired
    /// heading to <see cref="Route"/>, the agent paths a short lookahead of that heading along
    /// the baked mesh, and the bent direction goes back to be written as intent buttons. The
    /// sim remains the only thing that moves anybody — routing through the agent, actuation
    /// through the buttons, decision 38 with pathfinding attached.
    ///
    /// <para><b>It degrades to a straight wire.</b> No navmesh under the body, a destination
    /// the mesh cannot host, a still-pending path — every failure returns the caller's own
    /// heading unchanged, and the sim's ground seam goes on refusing illegal steps exactly as
    /// before. Headless tests never meet this component, so the strategies' pure paths stay
    /// pinned without it.</para>
    ///
    /// <para><b>Why a lookahead and not the target.</b> Routing all the way to a target would
    /// turn Zelda II's drunken menace into a homing missile — the bias toward the player
    /// belongs to the steering roll (<c>RoamSteering</c>), not the router. The oracle only
    /// answers "which way is THAT, along ground that exists?" for a few metres of intention,
    /// which bends wanderers around water, greebles and cliff feet, and up a ramp when one
    /// happens to lie along the wish.</para>
    /// </summary>
    public sealed class NavSteeringOracle : MonoBehaviour
    {
        /// <summary>Metres of intention handed to the agent per query. Short enough that the
        /// route stays a bend of the heading rather than a plan of its own.</summary>
        private const float Lookahead = 6f;

        /// <summary>How far SamplePosition may reach to land the lookahead point on the mesh.
        /// Generous vertically on purpose: a point wished into a cliff face resolves to the
        /// terrace above, which is how a ramp becomes worth walking toward.</summary>
        private const float SampleRadius = 4f;

        private const float RepathSeconds = 0.25f;
        private const float RepathMovedSq = 1.5f * 1.5f;

        private NavMeshAgent _agent;
        private Vector3 _lastWant;
        private Vector2 _lastRouted;
        private float _nextRepathAt;

        private void Awake()
        {
            _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.radius = 0.35f;
            _agent.height = 1.8f;
            // Speed never moves anything — only desiredVelocity's DIRECTION is read — but a
            // sane magnitude plus near-instant acceleration keeps that direction aligned with
            // the path instead of lagging a turn behind it.
            _agent.speed = 5f;
            _agent.acceleration = 60f;
            _agent.angularSpeed = 720f;
            _agent.autoBraking = false;   // the lookahead point is never meant to be arrived at
            _agent.Warp(transform.position);
        }

        private void Update()
        {
            // The sim moves the body; the agent is told about it after the fact. Off-mesh
            // (spawned before the bake, or carried somewhere strange) it re-binds by warping.
            if (_agent.isOnNavMesh) _agent.nextPosition = transform.position;
            else _agent.Warp(transform.position);
        }

        /// <summary>
        /// Bend <paramref name="desiredHeading"/> (XZ, any magnitude) along the walkable mesh.
        /// Returns a vector of the SAME magnitude in the routed direction — or the input
        /// unchanged whenever the mesh has no better answer.
        /// </summary>
        public Vector2 Route(Vector2 desiredHeading)
        {
            if (_agent == null || !_agent.isOnNavMesh) return desiredHeading;

            float magnitude = desiredHeading.magnitude;
            if (magnitude < 1e-4f) return desiredHeading;

            var direction = desiredHeading / magnitude;
            var want = transform.position +
                       new Vector3(direction.x, 0f, direction.y) * Lookahead;

            if (Time.time >= _nextRepathAt ||
                (want - _lastWant).sqrMagnitude > RepathMovedSq)
            {
                if (!NavMesh.SamplePosition(want, out var hit, SampleRadius, NavMesh.AllAreas))
                    return desiredHeading;   // wished into open water: let the sim refuse it

                _agent.SetDestination(hit.position);
                _lastWant = want;
                _nextRepathAt = Time.time + RepathSeconds;
            }

            if (_agent.pathPending)
                return _lastRouted.sqrMagnitude > 0f ? _lastRouted * magnitude : desiredHeading;

            var routed = new Vector2(_agent.desiredVelocity.x, _agent.desiredVelocity.z);
            if (routed.sqrMagnitude < 1e-4f) return desiredHeading;

            _lastRouted = routed.normalized;
            return _lastRouted * magnitude;
        }
    }
}
