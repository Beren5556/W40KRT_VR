#pragma once
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <stdexcept>
#include <string>
#include <cstring>

// All methods are called inside the backend's isolated context state. Views enforce the
// transfer function: sRGB SRV decodes to linear, sRGB RTV encodes from linear; alpha is linear.
// This header is also used by the deterministic GPU transfer test in the diagnostic host.
namespace rtn {
using Microsoft::WRL::ComPtr;
inline bool IsSrgb(DXGI_FORMAT f) { return f==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB||f==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB; }
inline bool IsTypelessColor(DXGI_FORMAT f) { return f==DXGI_FORMAT_R8G8B8A8_TYPELESS||f==DXGI_FORMAT_B8G8R8A8_TYPELESS; }
inline DXGI_FORMAT ColorViewFormat(DXGI_FORMAT f,bool semanticSrgb) {
 if(f==DXGI_FORMAT_R8G8B8A8_TYPELESS)return semanticSrgb?DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:DXGI_FORMAT_R8G8B8A8_UNORM;
 if(f==DXGI_FORMAT_B8G8R8A8_TYPELESS)return semanticSrgb?DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:DXGI_FORMAT_B8G8R8A8_UNORM;
 // A typed linear resource with an sRGB semantic is ambiguous; never silently reinterpret it.
 if(semanticSrgb&&!IsSrgb(f))throw std::runtime_error("sRGB semantics require an sRGB or typeless RGBA/BGRA resource");
 if(f!=DXGI_FORMAT_R16G16B16A16_FLOAT&&f!=DXGI_FORMAT_R32G32B32A32_FLOAT&&f!=DXGI_FORMAT_R8G8B8A8_UNORM&&f!=DXGI_FORMAT_B8G8R8A8_UNORM&&!IsSrgb(f))throw std::runtime_error("Unsupported color transfer format");
 return f;
}
inline void Check(HRESULT h,const char* what) { if(FAILED(h))throw std::runtime_error(std::string(what)+" HRESULT="+std::to_string(uint32_t(h))); }
inline ComPtr<ID3D11ShaderResourceView> ColorSrv(ID3D11Device* device,ID3D11Texture2D* texture,DXGI_FORMAT format) {
 D3D11_SHADER_RESOURCE_VIEW_DESC d{};d.Format=format;d.ViewDimension=D3D11_SRV_DIMENSION_TEXTURE2D;d.Texture2D.MipLevels=1;
 ComPtr<ID3D11ShaderResourceView> view;Check(device->CreateShaderResourceView(texture,&d,&view),"Color SRV");return view;
}
inline ComPtr<ID3D11RenderTargetView> ColorRtv(ID3D11Device* device,ID3D11Texture2D* texture,DXGI_FORMAT format) {
 D3D11_RENDER_TARGET_VIEW_DESC d{};d.Format=format;d.ViewDimension=D3D11_RTV_DIMENSION_TEXTURE2D;
 ComPtr<ID3D11RenderTargetView> view;Check(device->CreateRenderTargetView(texture,&d,&view),"Color RTV");return view;
}
class ColorTransfer {
 ComPtr<ID3D11VertexShader> vs;
 ComPtr<ID3D11PixelShader> ps;
 ComPtr<ID3D11Buffer> constants;
 ComPtr<ID3D11SamplerState> sampler;
 ComPtr<ID3D11RasterizerState> raster;
 ComPtr<ID3D11DepthStencilState> depth;
public:
 explicit ColorTransfer(ID3D11Device* device) {
  static const char* source=R"(
cbuffer Region : register(b0) { float4 rectangle; float4 tuning; }
Texture2D<float4> image : register(t0); SamplerState imageSampler : register(s0);
struct V { float4 position:SV_Position; float2 uv:TEXCOORD0; };
V VS(uint id:SV_VertexID) { V o; float2 uv=float2((id<<1)&2,id&2); o.position=float4(uv*float2(2,-2)+float2(-1,1),0,1); o.uv=uv; return o; }
float4 PS(V i):SV_Target {
 float2 uv=rectangle.xy+i.uv*rectangle.zw; float4 c=image.SampleLevel(imageSampler,uv,0);
 if(tuning.z>0) {
  float3 n=image.SampleLevel(imageSampler,uv+float2(0,tuning.y),0).rgb;
  float3 s=image.SampleLevel(imageSampler,uv-float2(0,tuning.y),0).rgb;
  float3 e=image.SampleLevel(imageSampler,uv+float2(tuning.x,0),0).rgb;
  float3 w=image.SampleLevel(imageSampler,uv-float2(tuning.x,0),0).rgb;
  float3 low=min(c.rgb,min(min(n,s),min(e,w))), high=max(c.rgb,max(max(n,s),max(e,w)));
  c.rgb=clamp(c.rgb+tuning.z*.5*(c.rgb-(n+s+e+w)*.25),low,high);
 } return c;
}
)";
  ComPtr<ID3DBlob> vertex,pixel,error;
  Check(D3DCompile(source,std::strlen(source),"RTNeural.ColorTransfer",nullptr,nullptr,"VS","vs_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&vertex,&error),"Compile transfer VS");
  Check(D3DCompile(source,std::strlen(source),"RTNeural.ColorTransfer",nullptr,nullptr,"PS","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&pixel,&error),"Compile transfer PS");
  Check(device->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&vs),"Transfer VS");
  Check(device->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&ps),"Transfer PS");
  D3D11_BUFFER_DESC b{};b.ByteWidth=32;b.Usage=D3D11_USAGE_DEFAULT;b.BindFlags=D3D11_BIND_CONSTANT_BUFFER;Check(device->CreateBuffer(&b,nullptr,&constants),"Transfer constants");
  D3D11_SAMPLER_DESC s{};s.Filter=D3D11_FILTER_MIN_MAG_MIP_POINT;s.AddressU=s.AddressV=s.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;s.MaxLOD=D3D11_FLOAT32_MAX;Check(device->CreateSamplerState(&s,&sampler),"Transfer sampler");
  D3D11_RASTERIZER_DESC r{};r.FillMode=D3D11_FILL_SOLID;r.CullMode=D3D11_CULL_NONE;r.DepthClipEnable=TRUE;Check(device->CreateRasterizerState(&r,&raster),"Transfer raster");
  D3D11_DEPTH_STENCIL_DESC z{};z.DepthEnable=FALSE;z.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ZERO;z.DepthFunc=D3D11_COMPARISON_ALWAYS;Check(device->CreateDepthStencilState(&z,&depth),"Transfer depth");
 }
 void Draw(ID3D11DeviceContext* c,ID3D11ShaderResourceView* source,ID3D11RenderTargetView* destination,
           UINT sourceWidth,UINT sourceHeight,UINT sourceX,UINT sourceY,UINT copyWidth,UINT copyHeight,
           UINT outputX,UINT outputY,UINT outputWidth,UINT outputHeight,float sharpness=0) {
  const float rect[]={float(sourceX)/sourceWidth,float(sourceY)/sourceHeight,float(copyWidth)/sourceWidth,float(copyHeight)/sourceHeight,1.f/sourceWidth,1.f/sourceHeight,sharpness,0};
  c->ClearState();c->UpdateSubresource(constants.Get(),0,nullptr,rect,0,0);
  c->VSSetShader(vs.Get(),nullptr,0);c->PSSetShader(ps.Get(),nullptr,0);auto b=constants.Get();c->PSSetConstantBuffers(0,1,&b);
  auto s=sampler.Get();c->PSSetSamplers(0,1,&s);c->PSSetShaderResources(0,1,&source);c->OMSetRenderTargets(1,&destination,nullptr);
  c->OMSetDepthStencilState(depth.Get(),0);c->RSSetState(raster.Get());
  const D3D11_VIEWPORT viewport{float(outputX),float(outputY),float(outputWidth),float(outputHeight),0,1};c->RSSetViewports(1,&viewport);
  c->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);c->Draw(3,0);c->ClearState();
 }
};
}
