using System.Collections;
using UnityEngine;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.Physics;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// Step A+.2 — embodied putting on the SAME first-person controller you scout and maintain with
    /// (register: My Summer Car / Sea of Thieves — simple, tactile, not a slick golf sim). The ball
    /// rolls per the Sim's BallPhysics, which reads the green's MAINTAINED Stimp/firmness and the real
    /// mesh slope — so a putt plays true to the conditions you made. Minimal: putt + short approach,
    /// a per-hole stroke tally. No clubs, no 18 holes, no fitting.
    ///
    /// SETUP: put on the Player; assign the ball Transform and (optional) cup Transform; the camera is
    /// the same one on the FP controller; greens need colliders the ray can hit.
    /// </summary>
    public sealed class PuttingController : MonoBehaviour
    {
        public GameManager game;
        public Camera cam;
        public Transform ball;
        public Transform cup;
        public float holeRadius = 0.12f;

        [Header("Input")]
        public KeyCode chargeKey = KeyCode.Mouse0;     // hold to build power, release to strike
        public KeyCode approachKey = KeyCode.Mouse1;   // hold for a short approach instead of a putt
        public float chargeRate = 0.6f;                // power per second while held
        public float worldFeet = 0.3048f;              // metres per foot (sim works in feet)

        public int Strokes { get; private set; }
        public float Power { get; private set; }
        public bool Holed { get; private set; }

        private bool _charging;
        private bool _approachMode;
        private bool _rolling;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (cam == null) cam = GetComponentInChildren<Camera>() ?? Camera.main;
        }

        private void Update()
        {
            if (_rolling || ball == null) return;

            if (UnityEngine.Input.GetKeyDown(chargeKey)) { _charging = true; _approachMode = false; Power = 0f; }
            if (UnityEngine.Input.GetKeyDown(approachKey)) { _charging = true; _approachMode = true; Power = 0f; }

            if (_charging)
            {
                Power = Mathf.PingPong(Time.time * chargeRate, 1f); // oscillating power meter — time your release
                if (UnityEngine.Input.GetKeyUp(chargeKey) || UnityEngine.Input.GetKeyUp(approachKey))
                {
                    _charging = false;
                    Strike(Power, _approachMode);
                }
            }
        }

        private void Strike(float power, bool approach)
        {
            ZoneState green = GreenUnderBall(out Vector3 surfaceNormal);
            if (green == null) return;
            Strokes++;

            // Aim = camera forward projected to the ground plane.
            Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            var aim = new Vec2(fwd.x, fwd.z);

            if (approach)
            {
                var res = BallPhysics.SolveApproach(green, new ApproachInput(power, aim), null);
                Vector3 dest = ball.position + new Vector3(fwd.x, 0, fwd.z) * (float)res.TotalFt * worldFeet;
                StartCoroutine(RollTo(dest, Mathf.Max(0.4f, (float)res.TotalFt / 12f), (float)res.FirstBounceFt * worldFeet));
            }
            else
            {
                // Slope gradient (downhill) from the mesh normal, in the (x,z) plane.
                Vector3 horiz = new Vector3(surfaceNormal.x, 0f, surfaceNormal.z);
                double grad = surfaceNormal.y > 1e-3f ? horiz.magnitude / surfaceNormal.y : 0.0;
                Vector3 downhill = horiz.sqrMagnitude > 1e-6f ? -horiz.normalized : Vector3.zero;
                var slopeGrad = new Vec2(downhill.x * grad, downhill.z * grad);

                var res = BallPhysics.SolvePutt(green, new PuttInput(power, aim, slopeGrad), null);
                Vector3 disp = new Vector3((float)res.Displacement.X, 0f, (float)res.Displacement.Y) * worldFeet;
                StartCoroutine(RollTo(ball.position + disp, Mathf.Max(0.3f, (float)res.RollSeconds), 0f));
            }
        }

        private IEnumerator RollTo(Vector3 worldTarget, float seconds, float bounce)
        {
            _rolling = true;
            Vector3 start = ball.position;
            float tElapsed = 0f;
            while (tElapsed < seconds)
            {
                tElapsed += Time.deltaTime;
                float k = Mathf.Clamp01(tElapsed / seconds);
                Vector3 p = Vector3.Lerp(start, worldTarget, Mathf.SmoothStep(0, 1, k));
                // Follow the green surface, plus a small arc for an approach bounce.
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
                    p.y = hit.point.y + 0.02f;
                p.y += bounce * Mathf.Sin(k * Mathf.PI) * (1f - k);
                ball.position = p;

                if (cup != null && Vector3.Distance(ball.position, cup.position) <= holeRadius) { Holed = true; break; }
                yield return null;
            }
            _rolling = false;
        }

        private ZoneState GreenUnderBall(out Vector3 normal)
        {
            normal = Vector3.up;
            if (ball == null || game == null || game.Course == null) return null;
            if (!Physics.Raycast(ball.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f)) return null;
            normal = hit.normal;
            var gr = hit.collider.GetComponentInParent<GreenRenderer>();
            return gr != null ? game.Course.Get(gr.zoneId) : null;
        }

        public void NextHole() { Strokes = 0; Holed = false; }

        // Exposed for the unified HUD to render the meter (no separate OnGUI panel).
        public bool Charging => _charging;
        public bool ApproachMode => _approachMode;
    }
}
