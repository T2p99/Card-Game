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
            var points = new[] { new Vector2(r.xMin + r.width * .12f, r.yMax),
                new Vector2(r.xMax - r.width * .12f, r.yMax), new Vector2(r.xMax, r.yMin + r.height * .23f),
                new Vector2(r.center.x, r.yMin), new Vector2(r.xMin, r.yMin + r.height * .23f) };
            var center = r.center;
            mesh.AddVert(center, new Color(.08f, .35f, .52f, .96f), Vector2.zero);
            foreach (var point in points) mesh.AddVert(point, color, Vector2.zero);
            for (var i = 0; i < points.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
        }
    }
}
