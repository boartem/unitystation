﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HealthV2;
using Systems.Electricity;
using Items;
using Items.Others;
using Objects.Machines;
using Doors;
using UnityEngine;
using TileManagement;

namespace Systems.Explosions
{
	public class ExplosionComponent : MonoBehaviour
	{
		[TooltipAttribute("If explosion radius has a degree of error equal to radius / 4")]
		public bool unstableRadius = false;
		[TooltipAttribute("Explosion Damage")]
		public int damage = 150;
		[TooltipAttribute("Explosion Radius in tiles")]
		public float radius = 4f;
		[TooltipAttribute("Shape of the explosion")]
		public EffectShapeType explosionType;
		[TooltipAttribute("Distance multiplied from explosion that will still shake = shakeDistance * radius")]
		public float shakeDistance = 8;
		[TooltipAttribute("generally necessary for smaller explosions = 1 - ((distance + distance) / ((radius + radius) + minDamage))")]
		public int minDamage = 2;
		[TooltipAttribute("Maximum duration grenade effects are visible depending on distance from center")]
		public float maxEffectDuration = .25f;
		[TooltipAttribute("Minimum duration grenade effects are visible depending on distance from center")]
		public float minEffectDuration = .05f;
		[TooltipAttribute("If explosion is actually EMP")]
		public bool isEmp = false;

		private LayerMask obstacleMask;

		/// <summary>
		/// Create explosion on selected matrix
		/// </summary>
		/// <param name="matrix"></param>
		public void Explode(Matrix matrix)
		{
			obstacleMask = LayerMask.GetMask( "Door Closed");
			StartCoroutine(ExplosionRoutine(matrix));
		}

		public void SetExplosionData(int damage = 150, float radius = 4f, bool isEmp = false, bool unstableRadius = false, EffectShapeType explosionType = EffectShapeType.Circle, float shakeDistance = 8, int minDamage = 2, float maxEffectDuration = .25f, float minEffectDuration = .05f)
		{
			this.damage = damage;
			this.radius = radius;
			this.isEmp = isEmp;
			this.unstableRadius = unstableRadius;
			this.explosionType = explosionType;
			this.shakeDistance = shakeDistance;
			this.minDamage = minDamage;
			this.maxEffectDuration = maxEffectDuration;
			this.minEffectDuration = minEffectDuration;
		}

		private IEnumerator ExplosionRoutine(Matrix matrix)
		{
			var explosionCenter = transform.position.RoundToInt();

			// First - play boom sound and shake ground
			PlaySoundAndShake(explosionCenter, isEmp);

			// Now let's create explosion shape
			int radiusInteger = (int)radius;
			var shape = EffectShape.CreateEffectShape(explosionType, explosionCenter, radiusInteger);

			var explosionCenter2d = explosionCenter.To2Int();
			var tileManager = GetComponentInParent<TileChangeManager>();
			var longestTime = 0f;

			foreach (var tilePos in shape)
			{
				float distance = Vector3Int.Distance(tilePos, explosionCenter);
				var tilePos2d = tilePos.To2Int();
				// Calculate damage from explosion
				int damage = CalculateDamage(tilePos2d, explosionCenter2d);
				// Calculate fire effect time
				var effectTime = DistanceFromCenter(explosionCenter2d, tilePos2d, minEffectDuration, maxEffectDuration);

				if (isEmp)
				{
					if (damage > 0)
					{
						EmpThings(tilePos, damage);
					}

					if (float.IsNaN(effectTime))
					{
						effectTime = 0f;
					}

					var localTilePos = MatrixManager.WorldToLocalInt(tilePos, matrix.Id);
					StartCoroutine(TimedEffect(localTilePos, effectTime, isEmp, tileManager));

					// Save longest effect time
					if (effectTime > longestTime)
						longestTime = effectTime;
				}
				else
				{
					// Is explosion goes behind walls?
					if (IsPastWall(explosionCenter2d, tilePos2d, distance))
					{
						// Heat the air
						matrix.ReactionManager.ExposeHotspotWorldPosition(tilePos2d, 1000);

						if (damage > 0)
						{
							// Damage poor living things
							DamageLivingThings(tilePos, damage);

							// Damage all objects
							DamageObjects(tilePos, damage);

							// Damage all tiles
							DamageTiles(tilePos, damage);
						}

						if (float.IsNaN(effectTime))
						{
							effectTime = 0f;
						}

						var localTilePos = MatrixManager.WorldToLocalInt(tilePos, matrix.Id);
						StartCoroutine(TimedEffect(localTilePos, effectTime, isEmp, tileManager));

						// Save longest effect time
						if (effectTime > longestTime)
							longestTime = effectTime;
					}
				}
			}

			// Wait until all fire effects are finished
			yield return WaitFor.Seconds(longestTime);

			Destroy(gameObject);
		}

		public IEnumerator TimedEffect(Vector3Int position, float time, bool isEmp, TileChangeManager tileChangeManager)
		{
			string effectName;
			OverlayType effectOverlayType;
			if (isEmp)
			{
				effectName = "EMPEffect";
				effectOverlayType = OverlayType.EMP;
			}
			else
			{
				effectName = "Fire";
				effectOverlayType = OverlayType.Fire;
			}
			//Dont add effect if it is already there
			if(tileChangeManager.MetaTileMap.HasOverlay(position, TileType.Effects, effectName)) yield break;

			tileChangeManager.MetaTileMap.AddOverlay(position, TileType.Effects, effectName);
			yield return WaitFor.Seconds(time);
			tileChangeManager.MetaTileMap.RemoveOverlaysOfType(position, LayerType.Effects, effectOverlayType);
		}

		private void EmpThing(GameObject thing, int EmpStrength)
		{
			if(thing != null)
			{
				if (isEmpAble(thing))
				{
					if (thing.TryGetComponent<ItemStorage>(out var storage))
					{
						foreach (var slot in storage.GetItemSlots())
						{
							EmpThing(slot.ItemObject, EmpStrength);
						}
					}

					if (thing.TryGetComponent<DynamicItemStorage>(out var dStorage))
					{
						foreach (var slot in dStorage.GetItemSlots())
						{
							EmpThing(slot.ItemObject, EmpStrength);
						}
					}

					var interfaces = thing.GetComponents<IEmpAble>();

					foreach (var EmpAble in interfaces)
					{
						EmpAble.OnEmp(EmpStrength);
					}
				}
			}
		}

		private void EmpThings(Vector3Int worldPosition, int damage)
		{
			foreach (var thing in MatrixManager.GetAt<Integrity>(worldPosition, true).Distinct())
			{
				EmpThing(thing.gameObject, damage);
			}

			foreach (var thing in MatrixManager.GetAt<LivingHealthMasterBase>(worldPosition, true).Distinct())
			{
				EmpThing(thing.gameObject, damage);
			}
		}

		private bool isEmpAble(GameObject thing)
		{
			if (thing.TryGetComponent<Machine>(out var machine))
			{
				if (machine.isEMPResistant) return false;
			}

			if (thing.TryGetComponent<ItemAttributesV2>(out var attributes))
			{
				if (Validations.HasItemTrait(thing, CommonTraits.Instance.EMPResistant)) return false;
			}

			return true;
		}

		private void DamageLivingThings(Vector3Int worldPosition, int damage)
		{
			var damagedLivingThings = (MatrixManager.GetAt<LivingHealthMasterBase>(worldPosition, true)
				//only damage each thing once
				.Distinct());

			foreach (var damagedLiving in damagedLivingThings)
			{
				damagedLiving.ApplyDamageAll(gameObject, damage, AttackType.Bomb, DamageType.Burn);
			}
		}

		private void DamageObjects(Vector3Int worldPosition, int damage)
		{
			var damagedObjects = (MatrixManager.GetAt<Integrity>(worldPosition, true)
				//only damage each thing once
				.Distinct());

			foreach (var damagedObject in damagedObjects)
			{
				damagedObject.ApplyDamage(damage, AttackType.Bomb, DamageType.Burn);
			}
		}

		private void DamageTiles(Vector3Int worldPosition, int damage)
		{
			var matrix = MatrixManager.AtPoint(worldPosition, true);
			matrix.MetaTileMap.ApplyDamage(MatrixManager.WorldToLocalInt(worldPosition, matrix), damage, worldPosition, AttackType.Bomb);
		}

		/// <summary>
		/// Plays explosion sound and shakes ground
		/// </summary>
		private void PlaySoundAndShake(Vector3Int explosionPosition, bool isEMP)
		{
			byte shakeIntensity = (byte)Mathf.Clamp(damage / 5, byte.MinValue, byte.MaxValue);
			ExplosionUtils.PlaySoundAndShake(explosionPosition, shakeIntensity, (int)shakeDistance, isEMP);
		}

		private bool IsPastWall(Vector2Int pos, Vector2Int damageablePos, float distance)
		{
			return MatrixManager.RayCast((Vector2)pos, damageablePos - pos, distance, LayerTypeSelection.Walls , obstacleMask).ItHit == false;
		}

		private int CalculateDamage(Vector2Int damagePos, Vector2Int explosionPos)
		{
			float distance = Vector2Int.Distance(explosionPos, damagePos);
			float effect = 1 - ((distance + distance) / ((radius + radius) + minDamage));
			return (int)(damage * effect);
		}

		/// <summary>
		/// calculates the distance from the the center using the looping x and y vars
		/// returns a float between the limits
		/// </summary>
		private float DistanceFromCenter(Vector2Int pos, Vector2Int center, float lowLimit = 0.05f, float highLimit = 0.25f)
		{
			var dif = center - pos;

			float percentage = (Mathf.Abs(dif.x) + Mathf.Abs(dif.y)) / (radius + radius);
			float reversedPercentage = (1 - percentage) * 100;
			float distance = ((reversedPercentage * (highLimit - lowLimit) / 100) + lowLimit);
			return distance;
		}
	}
}
