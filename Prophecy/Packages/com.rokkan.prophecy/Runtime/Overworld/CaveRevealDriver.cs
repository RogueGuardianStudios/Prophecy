using System.Collections.Generic;
using Rokkan.Prophecy.World;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The invert cutout's runtime half: watches which cave room the player is INSIDE
    /// (<see cref="OverworldCoverRules.ActiveCaveRegion"/> — the covered floor, not the roof
    /// above it), hides that room's roof pieces wholesale, and fades the invert. The mask
    /// itself lives in the geometry shaders (CaveInvertMask.hlsl), fed by four plain shader
    /// globals — take two of this design: the fullscreen depth-reconstruction pass it
    /// replaces died of an empty depth texture, and geometry fragments never needed any of
    /// that, they know their world positions exactly.
    ///
    /// <para>The void outside the world's silhouette is the CAMERA's own background: while
    /// the reveal runs, the clear fades to black and restores on the way out — including on
    /// teardown, so a scene transition mid-cave cannot strand a black sky.</para>
    ///
    /// <para>Attached by the grid host after its build; the editor preview never carries
    /// one, so the globals stay zeroed there and the shaders' invert branch is inert.</para>
    /// </summary>
    public sealed class CaveRevealDriver : MonoBehaviour
    {
        private const float FadePerSecond = 5f;

        private OverworldBuildOutput _built;
        private int _active = -1;
        private float _strength;

        private Camera _faded;
        private CameraClearFlags _originalClear;
        private Color _originalBackground;

        /// <summary>True while a room is active or the fade has not fully cleared — the
        /// halo cutout suppresses itself for the duration, because punching a hole through
        /// a revealed room's wall shows the void, and the reveal already guarantees the
        /// player is visible.</summary>
        public bool Revealing => _active >= 0 || _strength > 0.01f;

        public void Bind(OverworldBuildOutput built) => _built = built;

        private void Update()
        {
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
                    Shader.SetGlobalFloat("_CaveRoofY", RoomRoofY(region));
            }

            _strength = Mathf.MoveTowards(_strength, _active >= 0 ? 1f : 0f,
                                          FadePerSecond * Time.deltaTime);
            Shader.SetGlobalFloat("_CaveInvertStrength", _strength);
            Shader.SetGlobalFloat("_ActiveCoverRegion", _active + 1);

            FadeBackground();
        }

        /// <summary>The sky is not a fragment: the void beyond the world's silhouette comes
        /// from fading the camera's clear itself.</summary>
        private void FadeBackground()
        {
            if (_strength > 0.001f && _faded == null)
            {
                _faded = Camera.main;
                if (_faded == null) return;
                _originalClear = _faded.clearFlags;
                _originalBackground = _faded.backgroundColor;
            }

            if (_faded == null) return;

            if (_strength <= 0.001f)
            {
                _faded.clearFlags = _originalClear;
                _faded.backgroundColor = _originalBackground;
                _faded = null;
                return;
            }

            _faded.clearFlags = CameraClearFlags.SolidColor;
            _faded.backgroundColor = Color.Lerp(_originalBackground, Color.black, _strength);
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
            SetRoofVisible(_active, true);
            _active = -1;
            _strength = 0f;
            Shader.SetGlobalFloat("_CaveInvertStrength", 0f);
            Shader.SetGlobalFloat("_ActiveCoverRegion", 0f);
            if (_faded != null)
            {
                _faded.clearFlags = _originalClear;
                _faded.backgroundColor = _originalBackground;
                _faded = null;
            }
        }
    }
}
