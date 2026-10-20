using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Presentation
{
    public sealed class PortraitAttackArrow : MaskableGraphic
    {
        public Vector2 From, To;
        public void SetPoints(Vector2 from, Vector2 to)
        {
            From = from; To = to; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var delta = To - From;
            if (delta.magnitude < 12f) return;
            var direction = delta.normalized;
            var normal = new Vector2(-direction.y, direction.x);
            var stemEnd = To - direction * 21f;
            const int segments = 20;
            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float)segments;
                var point = Vector2.Lerp(From, stemEnd, t) + normal * Mathf.Sin(t * Mathf.PI) * delta.magnitude * 0.045f;
                mesh.AddVert(point - normal * 4f, color, Vector2.zero);
                mesh.AddVert(point + normal * 4f, color, Vector2.zero);
                if (i == 0) continue;
                var index = i * 2;
                mesh.AddTriangle(index - 2, index - 1, index);
                mesh.AddTriangle(index, index - 1, index + 1);
            }
            var tip = mesh.currentVertCount;
            mesh.AddVert(To, color, Vector2.zero);
            mesh.AddVert(stemEnd + normal * 16f, color, Vector2.zero);
            mesh.AddVert(stemEnd - normal * 16f, color, Vector2.zero);
            mesh.AddTriangle(tip, tip + 1, tip + 2);
        }
    }

}
