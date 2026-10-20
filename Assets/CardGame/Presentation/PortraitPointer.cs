using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.Presentation
{
    // Own the complete pointer sequence so a cancelled drag can never become
    // a click. Pointer events work with both mouse and Input System touch.
    public sealed class PortraitPointer : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IInitializePotentialDragHandler, ICancelHandler
    {
        public Action<Vector2> Press, Move, Release, Hold, Tap;
        public Action Cancel;
        public float LogicalScale = 1f;
        private static PortraitPointer _captured;
        private int _pointerId;
        private bool _pressed, _moved, _held;
        private float _pressedAt;
        private Vector2 _start, _position;

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || _captured != null) return;
            _captured = this;
            _pointerId = data.pointerId;
            _pressed = true;
            _moved = _held = false;
            _start = _position = data.position;
            _pressedAt = Time.unscaledTime;
            Press?.Invoke(_position);
        }
        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
        public void OnBeginDrag(PointerEventData data) { }
        public void OnDrag(PointerEventData data)
        {
            if (!_pressed || data.pointerId != _pointerId) return;
            _position = data.position;
            if (Vector2.Distance(_start, _position) > 10f * LogicalScale) _moved = true;
            Move?.Invoke(_position);
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (!_pressed || data.pointerId != _pointerId) return;
            _position = data.position;
            _pressed = false;
            _captured = null;
            // Up arrives before EndDrag. Commit at most once.
            if (_moved || _held) Release?.Invoke(_position);
            else Tap?.Invoke(_position);
        }
        public void OnEndDrag(PointerEventData data) { }
        private void Update()
        {
            if (!_pressed || _held || _moved || Time.unscaledTime - _pressedAt < 0.35f) return;
            _held = true;
            Hold?.Invoke(_position);
        }
        public void OnCancel(BaseEventData data) { CancelPress(); }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelPress(); }
        private void OnApplicationPause(bool paused) { if (paused) CancelPress(); }
        private void OnDisable() { CancelPress(); }
        private void CancelPress()
        {
            if (!_pressed) return;
            _pressed = false;
            if (_captured == this) _captured = null;
            Cancel?.Invoke();
        }
    }

}
