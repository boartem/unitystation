using System;
using System.Collections;
using System.Collections.Generic;
using AddressableReferences;
using Messages.Server;
using Mirror;
using UI.Objects.Shuttles;
using UnityEngine;
using UnityEngine.Events;

namespace Objects.Shuttles
{
	/// <summary>
	/// Main component for shuttle console
	/// </summary>
	public class ShuttleConsole : NetworkBehaviour, ICheckedInteractable<HandApply>, IServerSpawn
	{
		public MatrixMove ShuttleMatrixMove;
		private RegisterTile registerTile;
		private HasNetworkTab hasNetworkTab;

		[SerializeField] private AddressableAudioSource radarDetectionSound;
		public GUI_ShuttleControl GUItab;

		public ShuttleConsoleState shuttleConsoleState;

		private void Awake()
		{
			registerTile = GetComponent<RegisterTile>();
			hasNetworkTab = GetComponent<HasNetworkTab>();
		}

		public void OnSpawnServer(SpawnInfo info)
		{
			ShuttleMatrixMove = GetComponentInParent<MatrixMove>();

			if (ShuttleMatrixMove == null)
			{
				ShuttleMatrixMove = MatrixManager.Get(registerTile.Matrix).MatrixMove;
				if (ShuttleMatrixMove == null)
				{
					Logger.Log($"{this} is not on a movable matrix, so won't function.", Category.Shuttles);
					hasNetworkTab.enabled = false;
					return;
				}
				else
				{
					Logger.Log($"No MatrixMove reference set to {this}, found {ShuttleMatrixMove} automatically", Category.Shuttles);
				}
			}
			if (ShuttleMatrixMove.IsNotPilotable)
			{
				hasNetworkTab.enabled = false;
			}
			else
			{
				hasNetworkTab.enabled = true;
			}
		}

		public void PlayRadarDetectionSound()
		{
			_ = SoundManager.PlayNetworkedAtPosAsync(radarDetectionSound, gameObject.WorldPosServer(),
				default, default, default, default, gameObject);
		}

		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (!DefaultWillInteract.Default(interaction, side)) return false;
			//can only be interacted with an emag (normal click behavior is in HasNetTab)
			if (!Validations.HasItemTrait(interaction.UsedObject, CommonTraits.Instance.Emag)) return false;
			return true;
		}

		public void ServerPerformInteraction(HandApply interaction)
		{
			//apply emag
			if (shuttleConsoleState == ShuttleConsoleState.Normal)
			{
				shuttleConsoleState = ShuttleConsoleState.Emagged;
			}
			else if (shuttleConsoleState == ShuttleConsoleState.Emagged)
			{
				shuttleConsoleState = ShuttleConsoleState.Off;
			}
			else if (shuttleConsoleState == ShuttleConsoleState.Off)
			{
				shuttleConsoleState = ShuttleConsoleState.Normal;
			}
			if (GUItab)
			{
				GUItab.OnStateChange(shuttleConsoleState);
			}
		}

		/// <summary>
		/// Connects or disconnects a player from a shuttle rcs
		/// </summary>
		public void ChangeRcsPlayer(bool newState, PlayerScript playerScript)
		{
			var matrixMove = registerTile.Matrix.MatrixMove;

			if (newState)
			{
				playerScript.RcsMode = true;
				playerScript.RcsMatrixMove = matrixMove;
				matrixMove.playerControllingRcs = playerScript;
				matrixMove.rcsModeActive = true;
			}
			else
			{
				if (playerScript)
				{
					playerScript.RcsMode = false;
					playerScript.RcsMatrixMove = null;
				}
				matrixMove.playerControllingRcs = null;
				matrixMove.rcsModeActive = false;
			}

			matrixMove.CacheRcs();

			if (isServer)
			{
				if (GUItab)
				{
					GUItab.SetRcsLight(newState);
				}

				if(playerScript && playerScript != PlayerManager.LocalPlayerScript)
				{
					ShuttleRcsMessage.SendTo(this, newState, playerScript.connectedPlayer);
					playerScript.PlayerSync.RollbackPosition();
				}
			}
		}
	}

	public enum ShuttleConsoleState
	{
		Normal,
		Emagged,
		Off
	}

	/// <inheritdoc />
	/// "If you wish to use a generic UnityEvent type you must override the class type."
	[Serializable]
	public class TabStateEvent : UnityEvent<ShuttleConsoleState>
	{
	}
}
