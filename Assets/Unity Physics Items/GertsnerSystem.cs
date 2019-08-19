﻿using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics.Authoring;
using Unity.Transforms;
using UnityEngine;
using WaterSystem;
using WaterSystem.Data;

public class GertsnerSystem : JobComponentSystem
{
	private int _waveCount;
	private NativeArray<Wave> waveData;

	protected override void OnCreate()
	{
		_waveCount = Water.Instance._waves.Length;
		waveData = new NativeArray<Wave>(_waveCount, Allocator.Persistent);
		for (var i = 0; i < waveData.Length; i++)
		{
			waveData[i] = Water.Instance._waves[i];
		}

		base.OnCreate();
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var job = new HeightJob {
			waveData = waveData,
			time = Time.deltaTime,
			offsetBuffer = GetBufferFromEntity<VoxelOffset>(false),
			heightBuffer = GetBufferFromEntity<VoxelHeight>(false)
		};

		return job.Schedule(this, inputDeps);
	}

	[BurstCompile]
	public struct HeightJob : IJobForEachWithEntity<Translation, BuoyantData>
	{
		[ReadOnly]
		public NativeArray<Wave> waveData; // wave data stroed in vec4's like the shader version but packed into one

		[ReadOnly]
		public float time;

		[ReadOnly]
		public BufferFromEntity<VoxelOffset> offsetBuffer;

		[NativeDisableParallelForRestriction]
		public BufferFromEntity<VoxelHeight> heightBuffer;

		// The code actually running on the job
		public void Execute(Entity entity, int i, [ReadOnly] ref Translation translation, ref BuoyantData data)
		{
			DynamicBuffer<VoxelOffset> offsets = offsetBuffer[entity];
			DynamicBuffer<VoxelHeight> heights = heightBuffer[entity];

			for (int vi = 0; vi < offsets.Length; vi++)
			{
				float3 voxelPos = translation.Value + offsets[vi].Value;

				var waveCountMulti = 1f / waveData.Length;
				float3 wavePos = new float3(0f, 0f, 0f);
				float3 waveNorm = new float3(0f, 0f, 0f);

				for (var wave = 0; wave < waveData.Length; wave++) // for each wave
				{
					// Wave data vars
					var pos = new float2(voxelPos.x, voxelPos.z);

					var amplitude = waveData[wave].amplitude;
					var direction = waveData[wave].direction;
					var wavelength = waveData[wave].wavelength;
					var omniPos = waveData[wave].origin;
					////////////////////////////////wave value calculations//////////////////////////
					var w = 6.28318f / wavelength; // 2pi over wavelength(hardcoded)
					var wSpeed = math.sqrt(9.8f * w); // frequency of the wave based off wavelength
					var peak = 0.8f; // peak value, 1 is the sharpest peaks
					var qi = peak / (amplitude * w * waveData.Length);

					var windDir = new float2(0f, 0f);
					var dir = 0f;

					direction = math.radians(direction); // convert the incoming degrees to radians
					var windDirInput = new float2(math.sin(direction), math.cos(direction)) * (1 - waveData[wave].onmiDir); // calculate wind direction - TODO - currently radians
					var windOmniInput = (pos - omniPos) * waveData[wave].onmiDir;

					windDir += windDirInput;
					windDir += windOmniInput;
					windDir = math.normalize(windDir);
					dir = math.dot(windDir, pos - (omniPos * waveData[wave].onmiDir)); // calculate a gradient along the wind direction

					////////////////////////////position output calculations/////////////////////////
					var calc = dir * w + -time * wSpeed; // the wave calculation
					var cosCalc = math.cos(calc); // cosine version(used for horizontal undulation)
					var sinCalc = math.sin(calc); // sin version(used for vertical undulation)

					// calculate the offsets for the current point
					wavePos.x += qi * amplitude * windDir.x * cosCalc;
					wavePos.z += qi * amplitude * windDir.y * cosCalc;
					wavePos.y += ((sinCalc * amplitude)) * waveCountMulti; // the height is divided by the number of waves 

					if (offsets.Length == 1)
					{
						////////////////////////////normal output calculations/////////////////////////
						float wa = w * amplitude;
						// normal vector
						float3 norm = new float3(-(windDir.xy * wa * cosCalc),
										1 - (qi * wa * sinCalc));
						waveNorm += (norm * waveCountMulti) * amplitude;
					}
				}
				heights[vi] = new VoxelHeight{Value = wavePos};

				if (offsets.Length == 1)
					data.normal = math.normalize(waveNorm.xzy);
			}
		}
	}
}