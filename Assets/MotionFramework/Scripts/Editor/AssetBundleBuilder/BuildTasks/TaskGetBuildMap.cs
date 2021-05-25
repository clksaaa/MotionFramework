﻿//--------------------------------------------------
// Motion Framework
// Copyright©2021-2021 何冠峰
// Licensed under the MIT license
//--------------------------------------------------
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MotionFramework.Patch;

namespace MotionFramework.Editor
{
	internal class TaskGetBuildMap : IBuildTask
	{
		public class BuildMapContext : IContextObject
		{
			public readonly List<BundleInfo> BundleInfos = new List<BundleInfo>();

			/// <summary>
			/// 添加一个打包资源
			/// </summary>
			public void PackAsset(AssetInfo assetInfo)
			{
				if (TryGetBundleInfo(assetInfo.GetAssetBundleFullName(), out BundleInfo bundleInfo))
				{
					bundleInfo.PackAsset(assetInfo);
				}
				else
				{
					BundleInfo newBundleInfo = new BundleInfo(assetInfo.AssetBundleLabel, assetInfo.AssetBundleVariant);
					newBundleInfo.PackAsset(assetInfo);
					BundleInfos.Add(newBundleInfo);
				}
			}

			/// <summary>
			/// 获取所有的打包资源
			/// </summary>
			public List<AssetInfo> GetAllAssets()
			{
				List<AssetInfo> result = new List<AssetInfo>(BundleInfos.Count);
				foreach (var bundleInfo in BundleInfos)
				{
					result.AddRange(bundleInfo.Assets);
				}
				return result;
			}

			/// <summary>
			/// 检测哈希冲突并报告错误
			/// 注意：Unity的打包机制在指定了Variant的时候，同一个AssetBundle文件内不允许有同类型的同名文件
			/// </summary>
			public void CheckHashCollisionAndReportError()
			{
				Dictionary<string, string> temper = new Dictionary<string, string>(100);
				bool isThrowException = false;
				foreach (var bundleInfo in BundleInfos)
				{
					temper.Clear();
					string[] includeAssets = bundleInfo.GetIncludeAssetPaths();
					foreach (var assetPath in includeAssets)
					{
						string fileName = Path.GetFileName(assetPath);
						if (temper.ContainsKey(fileName))
						{
							isThrowException = true;
							string sameFile = temper[fileName];
							Debug.LogWarning($"Found same file in one assetBundle : {assetPath} {sameFile}");
						}
						else
						{
							temper.Add(fileName, assetPath);
						}
					}
				}
				if (isThrowException)
				{
					throw new Exception($"Found same file in assetBundle. Please see the warning information above.");
				}
			}

			/// <summary>
			/// 获取AssetBundle内包含的标记列表
			/// </summary>
			public string[] GetAssetTags(string bundleFullName)
			{
				if (TryGetBundleInfo(bundleFullName, out BundleInfo bundleInfo))
				{
					return bundleInfo.GetAssetTags();
				}
				throw new Exception($"Not found {nameof(BundleInfo)} : {bundleFullName}");
			}

			/// <summary>
			/// 获取AssetBundle内包含的资源路径列表
			/// </summary>
			public string[] GetIncludeAssetPaths(string bundleFullName)
			{
				if (TryGetBundleInfo(bundleFullName, out BundleInfo bundleInfo))
				{
					return bundleInfo.GetIncludeAssetPaths();
				}
				throw new Exception($"Not found {nameof(BundleInfo)} : {bundleFullName}");
			}

			/// <summary>
			/// 获取AssetBundle内收集的资源路径列表
			/// </summary>
			public string[] GetCollectAssetPaths(string bundleFullName)
			{
				if (TryGetBundleInfo(bundleFullName, out BundleInfo bundleInfo))
				{
					return bundleInfo.GetCollectAssetPaths();
				}
				throw new Exception($"Not found {nameof(BundleInfo)} : {bundleFullName}");
			}

			/// <summary>
			/// 获取构建管线里需要的数据
			/// </summary>
			public UnityEditor.AssetBundleBuild[] GetPipelineBuilds()
			{
				List<AssetBundleBuild> builds = new List<AssetBundleBuild>(BundleInfos.Count);
				foreach (var bundleInfo in BundleInfos)
				{
					builds.Add(bundleInfo.CreatePipelineBuild());
				}
				return builds.ToArray();
			}

			/// <summary>
			/// 获取所有的变种信息
			/// </summary>
			public List<PatchVariant> GetAllPatchVariant()
			{
				Dictionary<string, List<BundleInfo>> variantDic = new Dictionary<string, List<BundleInfo>>();
				foreach (var bundleInfo in BundleInfos)
				{
					string bundleLabel = bundleInfo.AssetBundleLabel;
					string bundleVariant = bundleInfo.AssetBundleVariant;

					if (variantDic.ContainsKey(bundleLabel) == false)
						variantDic.Add(bundleLabel, new List<BundleInfo>());

					if (bundleVariant != PatchDefine.AssetBundleDefaultVariant)
						variantDic[bundleLabel].Add(bundleInfo);
				}

				List<PatchVariant> result = new List<PatchVariant>();
				foreach (var pair in variantDic)
				{
					if (pair.Value.Count == 0)
						continue;

					string bundleName = $"{pair.Key}.{PatchDefine.AssetBundleDefaultVariant}";
					List<string> variants = pair.Value.Select(t => t.AssetBundleVariant.ToLower()).ToList();
					result.Add(new PatchVariant(bundleName.ToLower(), variants));
				}
				return result;
			}

			private bool TryGetBundleInfo(string bundleFullName, out BundleInfo result)
			{
				foreach (var bundleInfo in BundleInfos)
				{
					if (bundleInfo.AssetBundleFullName == bundleFullName)
					{
						result = bundleInfo;
						return true;
					}
				}
				result = null;
				return false;
			}
		}

		void IBuildTask.Run(BuildContext context)
		{
			List<AssetInfo> allAssets = GetBuildAssets();
			if (allAssets.Count == 0)
				throw new Exception("构建的资源列表不能为空");

			BuildLogger.Log($"构建的资源列表里总共有{allAssets.Count}个资源");
			BuildMapContext buildMapContext = new BuildMapContext();
			foreach (var assetInfo in allAssets)
			{
				buildMapContext.PackAsset(assetInfo);
			}
			context.SetContextObject(buildMapContext);

			// 最后检测哈希冲突
			buildMapContext.CheckHashCollisionAndReportError();
		}

		/// <summary>
		/// 获取构建的资源列表
		/// </summary>
		private List<AssetInfo> GetBuildAssets()
		{
			Dictionary<string, AssetInfo> buildAssets = new Dictionary<string, AssetInfo>();
			Dictionary<string, string> references = new Dictionary<string, string>();

			// 1. 获取主动收集的资源
			List<AssetCollectInfo> allCollectAssets = AssetBundleCollectorSettingData.GetAllCollectAssets();

			// 2. 对收集的资源进行依赖分析
			int progressValue = 0;
			foreach (AssetCollectInfo collectInfo in allCollectAssets)
			{
				string mainAssetPath = collectInfo.AssetPath;
				List<AssetInfo> depends = GetDependencies(mainAssetPath);
				for (int i = 0; i < depends.Count; i++)
				{
					AssetInfo assetInfo = depends[i];
					string assetPath = assetInfo.AssetPath;

					// 如果已经存在，则增加该资源的依赖计数
					if (buildAssets.ContainsKey(assetPath))
					{
						buildAssets[assetPath].DependCount++;
					}
					else
					{
						buildAssets.Add(assetPath, assetInfo);
						references.Add(assetPath, mainAssetPath);
					}

					// 添加资源标记
					buildAssets[assetPath].AddAssetTags(collectInfo.AssetTags);

					// 注意：检测是否为主动收集资源
					if (assetPath == mainAssetPath)
					{
						buildAssets[assetPath].IsCollectAsset = true;
						buildAssets[assetPath].DontWriteAssetPath = collectInfo.DontWriteAssetPath;
					}
				}
				EditorTools.DisplayProgressBar("依赖文件分析", ++progressValue, allCollectAssets.Count);
			}
			EditorTools.ClearProgressBar();

			// 3. 移除零依赖的资源
			List<AssetInfo> undependentAssets = new List<AssetInfo>();
			foreach (KeyValuePair<string, AssetInfo> pair in buildAssets)
			{
				if (pair.Value.IsCollectAsset)
					continue;
				if (pair.Value.DependCount == 0)
					undependentAssets.Add(pair.Value);
			}
			foreach (var assetInfo in undependentAssets)
			{
				buildAssets.Remove(assetInfo.AssetPath);
			}

			// 4. 设置资源标签和变种
			progressValue = 0;
			foreach (KeyValuePair<string, AssetInfo> pair in buildAssets)
			{
				var assetInfo = pair.Value;
				var bundleLabelAndVariant = AssetBundleCollectorSettingData.GetBundleLabelAndVariant(assetInfo.AssetPath);
				assetInfo.SetBundleLabelAndVariant(bundleLabelAndVariant.BundleLabel, bundleLabelAndVariant.BundleVariant);
				EditorTools.DisplayProgressBar("设置资源标签", ++progressValue, buildAssets.Count);
			}
			EditorTools.ClearProgressBar();

			// 5. 补充零依赖的资源
			foreach (var assetInfo in undependentAssets)
			{
				var referenceAssetPath = references[assetInfo.AssetPath];
				var referenceAssetInfo = buildAssets[referenceAssetPath];
				assetInfo.SetBundleLabelAndVariant(referenceAssetInfo.AssetBundleLabel, referenceAssetInfo.AssetBundleVariant);
				buildAssets.Add(assetInfo.AssetPath, assetInfo);
			}

			// 6. 返回结果
			return buildAssets.Values.ToList();
		}

		/// <summary>
		/// 获取指定资源依赖的资源列表
		/// 注意：返回列表里已经包括主资源自己
		/// </summary>
		private List<AssetInfo> GetDependencies(string mainAssetPath)
		{
			List<AssetInfo> result = new List<AssetInfo>();
			string[] depends = AssetDatabase.GetDependencies(mainAssetPath, true);
			foreach (string assetPath in depends)
			{
				if (AssetBundleCollectorSettingData.IsValidateAsset(assetPath))
				{
					AssetInfo assetInfo = new AssetInfo(assetPath);
					result.Add(assetInfo);
				}
			}
			return result;
		}
	}
}