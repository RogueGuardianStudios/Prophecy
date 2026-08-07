using System.Collections.Generic;
using Rokkan.Prophecy.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The invert cutout's runtime half: watches which cave room the player is INSIDE
    /// (<see cref="OverworldCoverRules.ActiveCaveRegion"/> — the covered floor, not the roof
    /// above it), hides that room's roof pieces wholesale, and fades the fullscreen pass that
    /// blacks the world outside the room. Attached by the grid host after its build; the
    /// editor preview never carries one, so the pass stays a plain copy there.
    ///
    /// <para><b>Everything the pass needs goes through its MATERIAL</b> (loaded from
    /// Resources — the one asset the renderer feature also references). Two traps live here,
    /// both paid for on 2026-08-07: texture GLOBALS never reach a RenderGraph fullscreen
    /// pass even though scalar globals do, and UNITY_MATRIX_I_VP is the blit's matrix, not
    /// the camera's — so the LUT binds to the material and the camera's inverse
    /// view-projection is handed over explicitly every frame.</para>
    ///
    /// <para>The roof swap is instant and the darkness is a fast fade — the pop of the roof
    /// coming off happens inside the swallow-to-black, which reads as entering, not as
    /// geometry vanishing. On teardown everything restores: roofs back on, the material
    /// zeroed, so a scene transition mid-cave cannot strand the world dark.</para>
    /// </summary>
    public sealed class CaveRevealDriver : MonoBehaviour
    {
        private const float FadePerSecond = 5f;

        private OverworldBuildOutput _built;
        private Material _passMaterial;
        private int _active = -1;
        private float _strength;

        public void Bind(OverworldBuildOutput built)
        {
            _built = built;
            _passMaterial = Resources.Load<Material>("CaveInvert");
            if (_passMaterial == null || _built.CoverLutTexture == null) return;

            var grid = _built.Grid;
            _passMaterial.SetTexture("_CoverLut", _built.CoverLutTexture);
            _passMaterial.SetVector("_CoverLutRect", new Vector4(
                grid.Origin.x, grid.Origin.y,
                1f / (grid.Width * OverworldTileGrid.CellSize),
                1f / (grid.Height * OverworldTileGrid.CellSize)));
        }

        private void Update()
        {
            if (_passMaterial == null) return;

            var director = SceneDirector.Instance;
            var player = director != null ? director.Player : null;

            int region = player != null && _built != null
                ? OverworldCoverRules.ActiveCaveRegion(_built.Grid, player.FeetWorldPosition)
                : -1;

            if (region != _active)
            {
                SetRoofVisible(_active, true);
                SetRoofVisible(region, false);
                _active = region;
                if (region >= 0)
                    _passMaterial.SetFloat("_CaveRoofY", RoomRoofY(region));
            }

            _strength = Mathf.MoveTowards(_strength, _active >= 0 ? 1f : 0f,
                                          FadePerSecond * Time.deltaTime);
            _passMaterial.SetFloat("_CaveInvertStrength", _strength);
            _passMaterial.SetFloat("_ActiveCoverRegion", _active + 1);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        /// <summary>
        /// The matrix hand-off happens HERE, per camera about to render — not in Update.
        /// The rig moves the camera in LateUpdate, so an Update-time matrix is one frame of
        /// camera motion stale, and the mask crawls against the image whenever anything
        /// moves. renderIntoTexture: TRUE — URP draws through an intermediate target when
        /// features are present, and ComputeClipSpacePosition's UV_STARTS_AT_TOP flip pairs
        /// with that convention; false "almost works" and smears the mask along view-Z.
        /// </summary>
        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_passMaterial == null) return;
            _passMaterial.SetMatrix("_CaveCamInvVP",
                (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true)
                 * camera.worldToCameraMatrix).inverse);
        }

        /// <summary>The room's roof plane: the highest base terrain over its cells. The
        /// shader refuses to reveal anything above it — the terrace tops around the room
        /// are the outside world, however close their cells sit to the mask's edge.</summary>
        private float RoomRoofY(int region)
        {
            var grid = _built.Grid;
            int highest = 1;
            for (int z = 0; z < grid.Height; z++)
                for (int x = 0; x < grid.Width; x++)
                    if (grid.CoverRegionAt(x, z) == region)
                        highest = Mathf.Max(highest, grid.LevelAt(x, z));
            return highest * OverworldTileGrid.Step;
        }

        private void SetRoofVisible(int region, bool visible)
        {
            if (region < 0 || _built == null) return;
            if (!_built.RoofByRegion.TryGetValue(region, out List<GameObject> roof)) return;

            for (int i = 0; i < roof.Count; i++)
                if (roof[i] != null)
                    roof[i].SetActive(visible);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            SetRoofVisible(_active, true);
            _active = -1;
            _strength = 0f;
            if (_passMaterial == null) return;
            _passMaterial.SetFloat("_CaveInvertStrength", 0f);
            _passMaterial.SetFloat("_ActiveCoverRegion", 0f);
            _passMaterial.SetTexture("_CoverLut", null);
        }
    }
}
