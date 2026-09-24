// Included inside the bridge namespace. Only Unity render callbacks touch
// queries; CPU readers copy completed results without waiting for the GPU.
struct MonitorQuery81 {
    ComPtr<ID3D11Query> frequency, begin, end;
    uint64_t serial=0, submitted=0;
    int kind=0;
    bool open=false, pending=false, invalid=false;
};
std::array<MonitorQuery81,32> monitorQueries81;
std::array<MonitorGpuTiming81,512> monitorReadings81{};
std::mutex monitorSnapshotGuard81;
unsigned monitorWrite81=0, monitorCount81=0;
uint64_t monitorTick81=0;
void ResetMonitor81() {
    for(auto& q:monitorQueries81)q={};
    std::lock_guard<std::mutex> lock(monitorSnapshotGuard81);
    monitorWrite81=monitorCount81=0; monitorTick81=0;
}
void PollMonitor81() {
    ++monitorTick81;
    for(auto& q:monitorQueries81) {
        if(!q.pending || monitorTick81-q.submitted<3)continue;
        D3D11_QUERY_DATA_TIMESTAMP_DISJOINT f{}; UINT64 b=0,e=0;
        HRESULT hf=context->GetData(q.frequency.Get(),&f,sizeof(f),D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if(hf==S_FALSE)continue;
        HRESULT hb=S_OK,he=S_OK;
        if(SUCCEEDED(hf)&&!f.Disjoint&&f.Frequency) {
            hb=context->GetData(q.begin.Get(),&b,sizeof(b),D3D11_ASYNC_GETDATA_DONOTFLUSH);
            he=context->GetData(q.end.Get(),&e,sizeof(e),D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if(hb==S_FALSE||he==S_FALSE)continue;
        }
        bool valid=!q.invalid&&SUCCEEDED(hf)&&SUCCEEDED(hb)&&SUCCEEDED(he)&&!f.Disjoint&&f.Frequency&&e>=b;
        std::unique_lock<std::mutex> snapshot(monitorSnapshotGuard81,std::try_to_lock);
        if(!snapshot.owns_lock())continue;
        monitorReadings81[monitorWrite81]={q.serial,q.kind,valid?1:0,valid?double(e-b)*1000.0/double(f.Frequency):-1.0};
        monitorWrite81=(monitorWrite81+1)%monitorReadings81.size();
        monitorCount81=std::min(monitorCount81+1,unsigned(monitorReadings81.size()));
        q.pending=false;
    }
}
void __stdcall MonitorEvent81(int eventId,void* data) {
    if(eventId<1||eventId>4)return;
    std::unique_lock<std::mutex> lock(guard,std::try_to_lock);
    if(!lock.owns_lock()||!device||!context)return;
    const uint64_t serial=uint64_t(reinterpret_cast<uintptr_t>(data));
    const int kind=(eventId+1)/2;
    if(!(eventId&1)) {
        for(auto& q:monitorQueries81)if(q.open&&q.serial==serial&&q.kind==kind) {
            context->End(q.end.Get()); context->End(q.frequency.Get());
            q.open=false;q.pending=true;q.submitted=monitorTick81;return;
        }
        return;
    }
    if(!diagnosticsEnabled.load(std::memory_order_relaxed))return;
    // Close missing end events as rejected, never attribute them to a new serial.
    for(auto& q:monitorQueries81)if(q.open) {
        context->End(q.end.Get());context->End(q.frequency.Get());
        q.open=false;q.pending=true;q.invalid=true;q.submitted=monitorTick81;
    }
    for(auto& q:monitorQueries81)if(!q.pending&&!q.open) {
        if(!q.frequency) {
            D3D11_QUERY_DESC f{D3D11_QUERY_TIMESTAMP_DISJOINT,0},t{D3D11_QUERY_TIMESTAMP,0};
            if(FAILED(device->CreateQuery(&f,&q.frequency))||FAILED(device->CreateQuery(&t,&q.begin))||FAILED(device->CreateQuery(&t,&q.end))) {q={};return;}
        }
        q.serial=serial;q.kind=kind;q.invalid=false;q.open=true;
        context->Begin(q.frequency.Get());context->End(q.begin.Get());return;
    }
}
