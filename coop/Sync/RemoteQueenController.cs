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
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance?._TileMgr != null && (GameMgr.Instance._TileMgr.m_MapLoading || GameMgr.Instance._TileMgr.m_GameLoading)) return;

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
                        bool shouldLoop = !animName.StartsWith("Attack") && !animName.StartsWith("Roll") &&
                                          !animName.StartsWith("TakeOut") && animName != "Jump";
                        var entry = Queen.m_Spine.AnimationState?.SetAnimation(0, animName, shouldLoop);
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
                        PlaySpineOneShotIfExists("Attack_Sword_1", "Attack_Sword");
                        break;
                    case Network.PlayerActionType.BowAttack:
                        PlaySpineOneShotIfExists("Attack_Bow_Mid", "Attack_Bow");
                        break;
                    case Network.PlayerActionType.Jump:
                        Queen.m_AniState = AniState.Jump;
                        break;
                }
            }
            catch { }
        }

        private void PlaySpineOneShotIfExists(params string[] candidates)
        {
            if (Queen?.m_Spine?.Skeleton?.Data == null || Queen.m_Spine.AnimationState == null) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                string anim = candidates[i];
                if (!string.IsNullOrEmpty(anim) && Queen.m_Spine.Skeleton.Data.FindAnimation(anim) != null)
                {
                    Queen.m_Spine.AnimationState.SetAnimation(0, anim, false);
                    _lastAnimName = anim;
                    return;
                }
            }
        }
    }
}
