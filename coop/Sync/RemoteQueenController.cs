using Spine.Unity;
using UnityEngine;

namespace RatopiaCoop.Sync
{
    public class RemoteQueenController : MonoBehaviour
    {
        public T_Queen Queen { get; private set; } = null!;

        private Vector3 _targetPosition;
        private Vector2 _targetVelocity;
        private float _lerpSpeed = 15f;
        private string _lastAnimName = string.Empty;

        public void Initialize(T_Queen queen)
        {
            Queen = queen;
            if (queen != null)
            {
                _targetPosition = queen.transform.position;
                if (!queen.gameObject.activeSelf)
                {
                    queen.gameObject.SetActive(true);
                }
            }
        }

        private void Update()
        {
            if (this == null || Queen == null || Queen.gameObject == null) return;

            try
            {
                // Smooth position interpolation
                transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * _lerpSpeed);

                // Keep unit's grid node synchronized in TileMgr
                Queen.NodeUpdate();
            }
            catch { }
        }

        public void ApplyNetworkState(Vector3 pos, Vector2 vel, string animName, bool facingRight, bool weaponOn)
        {
            if (this == null || Queen == null || Queen.gameObject == null) return;

            try
            {
                _targetPosition = pos;
                _targetVelocity = vel;

                // Snap if distance is large (initial spawn or teleport)
                if (transform != null && Vector3.Distance(transform.position, pos) > 5f)
                {
                    transform.position = pos;
                }

                // Ensure GameObject and MeshRenderer are active and visible
                if (!Queen.gameObject.activeSelf)
                {
                    Queen.gameObject.SetActive(true);
                }

                var mr = Queen.GetComponentInChildren<MeshRenderer>();
                if (mr != null && !mr.enabled)
                {
                    mr.enabled = true;
                }

                // Direction and weapon
                Queen.FlipX(facingRight);
                Queen.m_WeaponOn = weaponOn;

                // Spine Animation fallback
                if (string.IsNullOrEmpty(animName))
                {
                    animName = "Idle";
                }

                if (Queen.m_Spine != null && animName != _lastAnimName)
                {
                    _lastAnimName = animName;
                    try
                    {
                        var entry = Queen.m_Spine.AnimationState?.SetAnimation(0, animName, true);
                        if (entry != null)
                        {
                            entry.TimeScale = 1f;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void PlayAction(Network.PlayerActionType actionType)
        {
            if (Queen == null) return;

            try
            {
                switch (actionType)
                {
                    case Network.PlayerActionType.SwordAttack:
                        Queen.SwdAtk_Call();
                        break;
                    case Network.PlayerActionType.BowAttack:
                        Queen.BowAtk_Call();
                        break;
                    case Network.PlayerActionType.Jump:
                        Queen.m_AniState = AniState.Jump;
                        break;
                }
            }
            catch { }
        }
    }
}
