using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortraitShieldGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect;
            // Nested rectangular quads give a crisp luminous rim at any Canvas
            // scale, without a stretched low-resolution shield sprite.
            Quad(mesh, r, new Color(color.r, color.g, color.b, .16f), new Color(color.r, color.g, color.b, .32f));
            Quad(mesh, Inset(r, 2), new Color(.12f, .58f, .80f, .75f), color);
            Quad(mesh, Inset(r, 4), new Color(.035f, .16f, .25f, .98f), new Color(.08f, .43f, .61f, .98f));
            Quad(mesh, new Rect(r.xMin + 6, r.yMax - 7, Mathf.Max(0, r.width - 12), 2),
                new Color(.62f, .94f, 1, .9f), new Color(.62f, .94f, 1, .9f));
        }

        private static Rect Inset(Rect r, float padding) => new Rect(r.xMin + padding, r.yMin + padding,
            Mathf.Max(0, r.width - padding * 2), Mathf.Max(0, r.height - padding * 2));

        private static void Quad(VertexHelper mesh, Rect r, Color bottom, Color top)
        {
            var index = mesh.currentVertCount;
            mesh.AddVert(new Vector2(r.xMin, r.yMin), bottom, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMin, r.yMax), top, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax, r.yMax), top, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax, r.yMin), bottom, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
