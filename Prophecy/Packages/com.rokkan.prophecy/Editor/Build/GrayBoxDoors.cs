using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// The one way gray-box builders make doorways and room bounds — extracted the moment a
    /// second builder wanted them, because each builder carrying its own copy of this recipe
    /// is exactly how the tester's dummy got a floor-level hurtbox (the per-builder
    /// SetPrivate trap, recorded in the handoff).
    ///
    /// <para>A doorway is four things as one: the trigger the sim's transit commits at, the
    /// SEAL above the opening (a doorway you can jump over is a checkpoint), the amber frame
    /// (portal blue teleports, water blue sinks, door amber is a threshold), and the
    /// standing rule that the caller has put it on a LANDING PAD — flat ground both sides,
    /// because a committed crossing must never deliver into a drop.</para>
    /// </summary>
    internal static class GrayBoxDoors
    {
        /// <summary>Walk-through height. Above the stand height, below any jump arc that
        /// could clear a seal-less frame.</summary>
        public const float OpeningHeight = 2.6f;

        public static void Doorway(Transform parent, string name, float x,
                                   int roomMinSide, int roomMaxSide, float laneDepth,
                                   float ceilingY = 40f)
        {
            var door = new GameObject(name);
            door.transform.SetParent(parent, false);
            door.transform.position = new Vector3(x, OpeningHeight * 0.5f, 0f);

            var trigger = door.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.4f, OpeningHeight, laneDepth);

            var marker = door.AddComponent<RoomDoor>();
            GrayBoxSceneScaffold.SetPrivate(marker, "_roomMinSide", roomMinSide);
            GrayBoxSceneScaffold.SetPrivate(marker, "_roomMaxSide", roomMaxSide);

            // The seal: solid from the lintel to the ceiling, so the opening is the ONLY way.
            var seal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seal.name = name + "_Seal";
            seal.transform.SetParent(parent, false);
            seal.transform.position = new Vector3(x, (OpeningHeight + ceilingY) * 0.5f, 0f);
            seal.transform.localScale = new Vector3(0.4f, ceilingY - OpeningHeight, laneDepth);

            FramePiece(door.transform, "Frame_PostNear",
                       new Vector3(x, OpeningHeight * 0.5f, laneDepth * 0.5f + 0.25f),
                       new Vector3(0.35f, OpeningHeight, 0.5f));
            FramePiece(door.transform, "Frame_PostFar",
                       new Vector3(x, OpeningHeight * 0.5f, -laneDepth * 0.5f - 0.25f),
                       new Vector3(0.35f, OpeningHeight, 0.5f));
            FramePiece(door.transform, "Frame_Lintel",
                       new Vector3(x, OpeningHeight + 0.2f, 0f),
                       new Vector3(0.5f, 0.4f, laneDepth + 1f));
        }

        public static void Bounds(Transform parent, int room, float floorY, float ceilingY,
                                  float minX, float maxX)
        {
            var bounds = new GameObject($"RoomBounds_{room}");
            bounds.transform.SetParent(parent, false);

            var component = bounds.AddComponent<RoomBounds>();
            GrayBoxSceneScaffold.SetPrivate(component, "_room", room);
            GrayBoxSceneScaffold.SetPrivate(component, "_floorY", floorY);
            GrayBoxSceneScaffold.SetPrivate(component, "_ceilingY", ceilingY);
            GrayBoxSceneScaffold.SetPrivate(component, "_minX", minX);
            GrayBoxSceneScaffold.SetPrivate(component, "_maxX", maxX);
        }

        private static void FramePiece(Transform parent, string name, Vector3 position,
                                       Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = name;
            piece.transform.SetParent(parent, false);
            piece.transform.position = position;
            piece.transform.localScale = scale;
            Object.DestroyImmediate(piece.GetComponent<Collider>());
            piece.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Doorway();
        }
    }
}
