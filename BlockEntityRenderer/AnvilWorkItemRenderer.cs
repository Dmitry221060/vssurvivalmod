﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class AnvilWorkItemRenderer : IRenderer
    {
        private ICoreClientAPI api;
        private BlockPos pos;

        MeshRef workItemMeshRef;
        MeshRef recipeOutlineMeshRef;

        ItemStack ingot;
        int texId;

        Vec4f outLineColorMul = new Vec4f(1, 1, 1, 1);
        protected Matrixf ModelMat = new Matrixf();

        SurvivalCoreSystem coreMod;

        BlockEntityAnvil beAnvil;
        Vec4f glowRgb = new Vec4f();

        public AnvilWorkItemRenderer(BlockEntityAnvil beAnvil, BlockPos pos, ICoreClientAPI capi)
        {
            this.pos = pos;
            this.api = capi;
            this.beAnvil = beAnvil;

            coreMod = capi.ModLoader.GetModSystem<SurvivalCoreSystem>();
        }

        public double RenderOrder
        {
            get { return 0.5; }
        }

        public int RenderRange
        {
            get { return 24; }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (workItemMeshRef == null) return;
            if (stage == EnumRenderStage.AfterFinalComposition)
            {
                if (api.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemHammer)
                {
                    RenderRecipeOutLine();
                }
                return;
            }

            IRenderAPI rpi = api.Render;
            IClientWorldAccessor worldAccess = api.World;
            Vec3d camPos = worldAccess.Player.Entity.CameraPos;
            int temp = (int)ingot.Collectible.GetTemperature(api.World, ingot);

            Vec4f lightrgbs = worldAccess.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
            int extraGlow = GameMath.Clamp((temp - 550) / 2, 0, 255);
            float[] glowColor = ColorUtil.GetIncandescenceColorAsColor4f(temp);
            glowRgb.R = glowColor[0];
            glowRgb.G = glowColor[1];
            glowRgb.B = glowColor[2];
            glowRgb.A = extraGlow / 255f;

            rpi.GlDisableCullFace();

            IShaderProgram prog = coreMod.anvilShaderProg;
            prog.Use();
            rpi.BindTexture2d(texId);
            prog.Uniform("rgbaAmbientIn", rpi.AmbientColor);

            prog.Uniform("rgbaFogIn", rpi.FogColor);
            prog.Uniform("fogMinIn", rpi.FogMin);
            prog.Uniform("dontWarpVertices", (int)0);
            prog.Uniform("addRenderFlags", (int)0);
            prog.Uniform("fogDensityIn", rpi.FogDensity);
            prog.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
            prog.Uniform("rgbaLightIn", lightrgbs);
            prog.Uniform("rgbaGlowIn", glowRgb);
            prog.Uniform("extraGlow", extraGlow);
            
            prog.UniformMatrix("modelMatrix", ModelMat
                .Identity()
                .Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z)
                .Values
            );
            prog.UniformMatrix("viewMatrix", rpi.CameraMatrixOriginf);
            prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);


            rpi.RenderMesh(workItemMeshRef);

            prog.Stop();
        }



        private void RenderRecipeOutLine()
        {
            if (recipeOutlineMeshRef == null || api.HideGuis) return;
            IRenderAPI rpi = api.Render;
            IClientWorldAccessor worldAccess = api.World;
            EntityPos plrPos = worldAccess.Player.Entity.Pos;
            Vec3d camPos = worldAccess.Player.Entity.CameraPos;
            ModelMat.Set(rpi.CameraMatrixOriginf).Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z);
            outLineColorMul.A = 1 - GameMath.Clamp((float)Math.Sqrt(plrPos.SquareDistanceTo(pos.X, pos.Y, pos.Z)) / 5 - 1f, 0, 1);

            rpi.LineWidth = 2;
            rpi.GLEnableDepthTest();
            rpi.GlToggleBlend(true);

            IShaderProgram prog = rpi.GetEngineShader(EnumShaderProgram.Wireframe);

            prog.Use();
            prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);
            prog.UniformMatrix("modelViewMatrix", ModelMat.Values);
            prog.Uniform("colorIn", outLineColorMul);
            rpi.RenderMesh(recipeOutlineMeshRef);
            prog.Stop();
        }



        public void RegenMesh(ItemStack workitemStack, byte[,,] voxels, bool[,,] recipeToOutlineVoxels)
        {
            workItemMeshRef?.Dispose();
            workItemMeshRef = null;
            this.ingot = workitemStack;

            if (workitemStack == null) return;

            ObjectCacheUtil.Delete(api, "" + workitemStack.Attributes.GetInt("meshRefId"));
            workitemStack.Attributes.RemoveAttribute("meshRefId");

            if (recipeToOutlineVoxels != null)
            {
                RegenOutlineMesh(recipeToOutlineVoxels, voxels);
            }

            MeshData workItemMesh = ItemWorkItem.GenMesh(api, workitemStack, voxels, out texId);

            workItemMeshRef = api.Render.UploadMesh(workItemMesh);
        }


        private void RegenOutlineMesh(bool[,,] recipeToOutlineVoxels, byte[,,] voxels)
        {
            MeshData recipeOutlineMesh = new MeshData(24, 36, false, false, true, false);
            recipeOutlineMesh.SetMode(EnumDrawMode.Lines);

            int greenCol = (170 << 24) | (100 << 16) | (200 << 8) | (100);
            int orangeCol = (220 << 24) | (75 << 16) | (0 << 8) | (47);
            MeshData greenVoxelMesh = LineMeshUtil.GetCube(greenCol);
            MeshData orangeVoxelMesh = LineMeshUtil.GetCube(orangeCol);
            for (int i = 0; i < greenVoxelMesh.xyz.Length; i++)
            {
                greenVoxelMesh.xyz[i] = greenVoxelMesh.xyz[i] / 32f + 1 / 32f;
                orangeVoxelMesh.xyz[i] = orangeVoxelMesh.xyz[i] / 32f + 1 / 32f;
            }
            MeshData voxelMeshOffset = greenVoxelMesh.Clone();


            int yMax = recipeToOutlineVoxels.GetLength(1);

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 6; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        bool requireMetalHere = y >= yMax ? false : recipeToOutlineVoxels[x, y, z];

                        EnumVoxelMaterial mat = (EnumVoxelMaterial)voxels[x, y, z];

                        if (requireMetalHere && mat == EnumVoxelMaterial.Metal) continue;
                        if (!requireMetalHere && mat == EnumVoxelMaterial.Empty) continue;

                        float px = x / 16f;
                        float py = 10/16f + y / 16f;
                        float pz = z / 16f;

                        for (int i = 0; i < greenVoxelMesh.xyz.Length; i += 3)
                        {
                            voxelMeshOffset.xyz[i] = px + greenVoxelMesh.xyz[i];
                            voxelMeshOffset.xyz[i + 1] = py + greenVoxelMesh.xyz[i + 1];
                            voxelMeshOffset.xyz[i + 2] = pz + greenVoxelMesh.xyz[i + 2];
                        }

                        voxelMeshOffset.Rgba = (requireMetalHere && mat == EnumVoxelMaterial.Empty) ? greenVoxelMesh.Rgba : orangeVoxelMesh.Rgba;

                        recipeOutlineMesh.AddMeshData(voxelMeshOffset);
                    }
                }
            }

            recipeOutlineMeshRef?.Dispose();
            recipeOutlineMeshRef = null;
            if (recipeOutlineMesh.VerticesCount > 0)
            {
                recipeOutlineMeshRef = api.Render.UploadMesh(recipeOutlineMesh);
            }
        }


        public void Dispose()
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            api.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);

            recipeOutlineMeshRef?.Dispose();
            workItemMeshRef?.Dispose();
        }
    }
}
