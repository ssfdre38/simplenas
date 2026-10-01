// SimpleNAS Premium Frontend
const API_BASE = window.location.origin + '/api';

// Tab switching
function showTab(tabName) {
    document.querySelectorAll('.tab-content').forEach(tab => tab.classList.add('hidden'));
    document.getElementById(`tab-${tabName}`).classList.remove('hidden');
    
    document.querySelectorAll('.nav-btn').forEach(btn => btn.classList.remove('nav-active'));
    document.querySelector(`[data-tab="${tabName}"]`).classList.add('nav-active');
    
    // Load data for the tab
    switch(tabName) {
        case 'dashboard':
            loadDashboard();
            break;
        case 'zfs':
            loadZFSPools();
            loadZfsDatasets();
            loadZfsSnapshots();
            loadZfsDevices();
            loadSnapshotSchedule();
            break;
        case 'shares':
            loadShares();
            break;
        case 'cloud':
            loadCloud();
            break;
        case 'network':
            loadNetwork();
            loadFirewallStatus();
            loadSslStatus();
            break;
        case 'users':
            loadUsers();
            break;
        case 'plugins':
            loadPlugins();
            break;
        case 'files':
            loadFileRoots();
            loadFiles(window.currentBrowserPath || '');
            break;
        case 'logs':
            loadLogs();
            break;
    }
}

// Circular progress helper
function setProgress(id, percent) {
    const circle = document.getElementById(`${id}-circle`);
    if (!circle) return;
    const circumference = 301.5; // 2 * pi * 48
    const offset = circumference - (percent / 100) * circumference;
    circle.style.strokeDashoffset = offset;
}

// Dashboard Refresh
async function loadDashboard() {
    try {
        const response = await fetch(`${API_BASE}/system/status`);
        const data = await response.json();
        
        window.nasPlatform = data.platform || 'linux';
        if (window.nasPlatform === 'windows') {
            const storageTitle = document.getElementById('storage-center-title');
            if (storageTitle) storageTitle.textContent = 'Windows Storage Center';
            const storageDesc = document.getElementById('storage-center-desc');
            if (storageDesc) storageDesc.textContent = 'Manage Windows storage volumes, drives, and directory datasets';
            const poolsHeading = document.getElementById('storage-pools-heading');
            if (poolsHeading) poolsHeading.textContent = 'Windows Storage Pools & Volumes';
            const dsHeading = document.getElementById('storage-datasets-heading');
            if (dsHeading) dsHeading.textContent = 'Directory Datasets & Mounts';
            const snapHeading = document.getElementById('storage-snapshots-heading');
            if (snapHeading) snapHeading.textContent = 'Storage Snapshots';
            const navLabel = document.getElementById('nav-storage-label');
            if (navLabel) navLabel.textContent = 'Storage & Drives';
            const sharesHeading = document.getElementById('shares-smb-heading');
            if (sharesHeading) sharesHeading.textContent = 'Windows SMB File Sharing';
        }
        
        document.getElementById('cpu-usage').textContent = `${data.cpu.percent.toFixed(0)}%`;
        setProgress('cpu', data.cpu.percent);
        document.getElementById('cpu-core-details').textContent = `Sys Load Avg: ${data.cpu.percent.toFixed(1)}%`;
        
        document.getElementById('mem-usage').textContent = `${data.memory.percent.toFixed(0)}%`;
        setProgress('mem', data.memory.percent);
        document.getElementById('mem-ram-details').textContent = `RAM Allocation: ${data.memory.percent.toFixed(1)}%`;
        
        const diskPercent = parseFloat(data.disk.percent.replace('%', '')) || 0;
        document.getElementById('disk-usage').textContent = `${diskPercent.toFixed(0)}%`;
        setProgress('disk', diskPercent);
        document.getElementById('disk-pool-details').textContent = `Root Space Used: ${data.disk.percent}`;
        
        // Load services controls
        const servicesResp = await fetch(`${API_BASE}/system/services`);
        const servicesData = await servicesResp.json();
        
        const servicesList = document.getElementById('services-list');
        servicesList.innerHTML = '';
        
        for (const [name, status] of Object.entries(servicesData.services)) {
            const isRunning = status === 'active';
            const statusClass = isRunning ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20' : 'text-rose-400 bg-rose-500/10 border-rose-500/20';
            const statusText = isRunning ? 'Active' : 'Stopped';
            const pulseDot = isRunning ? 'bg-emerald-400 animate-pulse' : 'bg-rose-500';
            
            servicesList.innerHTML += `
                <div class="glass-card bg-slate-900/40 p-4 rounded-xl border border-slate-800 flex justify-between items-center">
                    <div>
                        <span class="font-bold text-white text-base tracking-wide">${name.toUpperCase()}</span>
                        <div class="flex items-center space-x-2 mt-1">
                            <span class="w-1.5 h-1.5 rounded-full ${pulseDot}"></span>
                            <span class="text-[10px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded border ${statusClass}">${statusText}</span>
                        </div>
                    </div>
                    <div class="flex items-center space-x-4">
                        <!-- Toggle switch -->
                        <div class="flex items-center">
                            <input type="checkbox" id="svc-${name}" onchange="toggleService('${name}', this)" class="hidden switch-input" ${isRunning ? 'checked' : ''} />
                            <label for="svc-${name}" class="switch-label relative inline-block w-11 h-6 bg-slate-800 border border-slate-700 rounded-full cursor-pointer transition-all duration-300">
                                <span class="switch-dot absolute top-0.5 left-0.5 inline-block w-5 h-5 bg-white rounded-full transition-transform duration-300 shadow"></span>
                            </label>
                        </div>
                        <!-- Restart button -->
                        <button onclick="controlService('${name}', 'restart')" class="p-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white border border-slate-700/50 transition-all" title="Restart Service">
                            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 1121.21 7.89M9 11l3-3m0 0l3 3m-3-3v8"></path>
                            </svg>
                        </button>
                    </div>
                </div>
            `;
        }
    } catch (error) {
        console.error('Dashboard load error:', error);
    }
}

// Service Controls Actions
async function toggleService(name, element) {
    const action = element.checked ? 'start' : 'stop';
    await controlService(name, action);
}

async function controlService(name, action) {
    try {
        const response = await fetch(`${API_BASE}/system/services/control`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({service: name, action})
        });
        if (response.ok) {
            loadDashboard();
        } else {
            const err = await response.json();
            alert(`Failed to execute service control: ${err.error || 'Unknown error'}`);
            loadDashboard(); // Revert toggle visually
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
        loadDashboard();
    }
}

// ZFS Pools
async function loadZFSPools() {
    try {
        const [poolsResp, smartResp] = await Promise.all([
            fetch(`${API_BASE}/zfs/pools`),
            fetch(`${API_BASE}/storage/smart`).catch(() => null)
        ]);

        const data = await poolsResp.json();
        const smartReports = smartResp && smartResp.ok ? await smartResp.json() : [];
        
        const poolsList = document.getElementById('pools-list');
        poolsList.innerHTML = '';
        
        if (data.pools.length === 0) {
            poolsList.innerHTML = '<p class="text-slate-500 text-sm">No ZFS pools found. Create a pool to get started.</p>';
            return;
        }
        
        data.pools.forEach((pool, idx) => {
            const isOnline = pool.health === 'ONLINE';
            const healthClass = isOnline ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20' : 'text-rose-400 bg-rose-500/10 border-rose-500/20';
            const isWindowsDrive = pool.name.startsWith('Windows_') || pool.name.startsWith('Drive_') || pool.name.startsWith('Google_');
            
            // Find SMART record
            const cleanPoolName = pool.name.replace('Windows_', '').replace('Drive_', '').replace('Google_Drive_', '');
            const smart = (smartReports && smartReports.length > 0) 
                ? (smartReports.find(s => s.deviceId.includes(cleanPoolName) || s.model.includes(cleanPoolName)) || smartReports[idx % smartReports.length])
                : null;

            const smartHtml = smart ? `
                <div class="flex flex-wrap items-center gap-1.5 pt-1.5 text-[11px] font-mono">
                    <span class="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">S.M.A.R.T: ${smart.healthStatus}</span>
                    <span class="px-2 py-0.5 rounded bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">🔥 ${smart.temperatureC}°C</span>
                    <span class="px-2 py-0.5 rounded bg-purple-500/10 text-purple-400 border border-purple-500/20">${smart.mediaType}</span>
                    <span class="text-slate-500 text-[10px] hidden sm:inline">Hours: ${smart.powerOnHours}h</span>
                </div>
            ` : '';

            const actionsHtml = isWindowsDrive
                ? `<button onclick="alert('Host drive volume ${pool.name} is active and online with full NTFS/Windows integrity.')" class="bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/20 text-[10px] font-bold px-3 py-1.5 rounded-lg transition-all">Volume Status: Active</button>`
                : `<button onclick="scrubPool('${pool.name}')" class="bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/20 text-[10px] font-bold px-3 py-1.5 rounded-lg transition-all">Scrub Pool</button>
                   <button onclick="destroyPool('${pool.name}')" class="bg-rose-500/10 hover:bg-rose-500/20 text-rose-500 border border-rose-500/20 text-[10px] font-bold px-3 py-1.5 rounded-lg transition-all">Destroy Pool</button>`;
            
            poolsList.innerHTML += `
                <div class="bg-slate-900/40 border border-slate-800 p-5 rounded-xl space-y-3">
                    <div class="flex justify-between items-center">
                        <div>
                            <h4 class="text-lg font-bold text-white">${pool.name}</h4>
                            <p class="text-xs text-slate-400">Total Capacity: ${pool.size}</p>
                            ${smartHtml}
                        </div>
                        <span class="text-xs font-semibold px-2.5 py-1 rounded-lg border ${healthClass}">${pool.health}</span>
                    </div>
                    
                    <div class="grid grid-cols-3 gap-2 text-center text-xs">
                        <div class="p-2 bg-slate-950/40 rounded-lg">
                            <span class="block text-slate-400">Allocated</span>
                            <span class="block font-bold text-slate-200 mt-1">${pool.allocated}</span>
                        </div>
                        <div class="p-2 bg-slate-950/40 rounded-lg">
                            <span class="block text-slate-400">Free</span>
                            <span class="block font-bold text-slate-200 mt-1">${pool.free}</span>
                        </div>
                        <div class="p-2 bg-slate-950/40 rounded-lg">
                            <span class="block text-slate-400">Usage %</span>
                            <span class="block font-bold text-slate-200 mt-1">${pool.capacity}</span>
                        </div>
                    </div>
                    
                    <div class="flex justify-end gap-2 mt-4 pt-3 border-t border-slate-800/40">
                        ${actionsHtml}
                    </div>
                </div>
            `;
        });
    } catch (error) {
        console.error('ZFS load error:', error);
    }
}

// ZFS Datasets
async function loadZfsDatasets() {
    try {
        const response = await fetch(`${API_BASE}/zfs/datasets`);
        const data = await response.json();
        
        const datasetsList = document.getElementById('datasets-list');
        if (data.datasets.length === 0) {
            datasetsList.innerHTML = '<p class="text-slate-500 text-sm">No datasets found</p>';
            return;
        }
        
        let tableHtml = `
            <table class="w-full text-left text-xs border-collapse">
                <thead>
                    <tr class="border-b border-slate-800 text-slate-400">
                        <th class="py-2">Dataset Name</th>
                        <th class="py-2">Used</th>
                        <th class="py-2">Available</th>
                        <th class="py-2">Mount Point</th>
                        <th class="py-2 text-right">Actions</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-850">
        `;
        
        data.datasets.forEach(ds => {
            tableHtml += `
                <tr class="text-slate-300">
                    <td class="py-3 font-semibold text-slate-200">${ds.name}</td>
                    <td class="py-3 font-mono">${ds.used}</td>
                    <td class="py-3 font-mono">${ds.avail}</td>
                    <td class="py-3 font-mono text-slate-400">${ds.mountpoint}</td>
                    <td class="py-3 text-right">
                        <button onclick="deleteDataset('${ds.name}')" class="text-rose-500 hover:text-rose-400 font-bold bg-rose-500/10 hover:bg-rose-500/20 px-2 py-1 rounded border border-rose-500/20 transition-all text-[10px]">
                            Delete
                        </button>
                    </td>
                </tr>
            `;
        });
        
        tableHtml += `</tbody></table>`;
        datasetsList.innerHTML = tableHtml;
    } catch (error) {
        console.error('Dataset load error:', error);
    }
}

// ZFS Raw Devices & Drive Grid
async function loadZfsDevices() {
    try {
        const response = await fetch(`${API_BASE}/zfs/devices`);
        const data = await response.json();
        
        const availableDisks = document.getElementById('available-disks');
        availableDisks.innerHTML = '';
        
        if (data.devices.length === 0) {
            availableDisks.innerHTML = '<p class="text-slate-500 text-sm">No raw disks detected</p>';
            return;
        }
        
        data.devices.forEach(dev => {
            availableDisks.innerHTML += `
                <div class="flex items-center space-x-3 p-3 bg-slate-900/40 border border-slate-850 rounded-xl">
                    <div class="p-2 bg-slate-950/40 rounded-lg text-amber-500">
                        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10"></path>
                        </svg>
                    </div>
                    <div>
                        <span class="block font-bold text-slate-200 text-sm font-mono">${dev.name}</span>
                        <span class="block text-slate-450 text-[10px]">Disk Capacity: ${dev.size}</span>
                    </div>
                </div>
            `;
        });
    } catch (e) {
        console.error('Devices load error:', e);
    }
}

async function showCreatePool() {
    // Load available devices for checkbox selections
    const response = await fetch(`${API_BASE}/zfs/devices`);
    const data = await response.json();
    
    const devicesList = document.getElementById('devices-list');
    devicesList.innerHTML = '';
    
    if (data.devices.length === 0) {
        devicesList.innerHTML = '<p class="text-slate-500 text-xs py-2">No raw hard drives available</p>';
    } else {
        data.devices.forEach(device => {
            devicesList.innerHTML += `
                <label class="flex items-center space-x-3 p-2 bg-slate-950/20 hover:bg-slate-950/40 rounded-lg border border-slate-800/50 cursor-pointer">
                    <input type="checkbox" name="devices" value="${device.name}" class="h-4 w-4 bg-slate-900 border-slate-700 text-cyan-500 rounded">
                    <span class="text-xs font-mono font-semibold text-slate-200">${device.name} (${device.size})</span>
                </label>
            `;
        });
    }
    
    document.getElementById('create-pool-modal').classList.remove('hidden');
}

function hideCreatePool() {
    document.getElementById('create-pool-modal').classList.add('hidden');
}

async function createPool(event) {
    event.preventDefault();
    
    const name = document.getElementById('pool-name').value;
    const vdev_type = document.getElementById('pool-type').value;
    const devices = Array.from(document.querySelectorAll('input[name="devices"]:checked'))
        .map(cb => cb.value);
    
    if (devices.length === 0) {
        alert('Please select at least one hard drive');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/zfs/pools`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({name, vdev_type, devices})
        });
        
        if (response.ok) {
            alert('ZFS Pool created successfully!');
            hideCreatePool();
            loadZFSPools();
            loadZfsDatasets();
            loadZfsDevices();
        } else {
            const error = await response.json();
            alert(`Failed to create pool: ${error.detail || 'Internal error'}`);
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

// ZFS Import actions
async function showImportPool() {
    try {
        const response = await fetch(`${API_BASE}/zfs/importable`);
        const data = await response.json();
        
        const listDiv = document.getElementById('importable-pools-list');
        listDiv.innerHTML = '';
        
        if (data.pools.length === 0) {
            listDiv.innerHTML = '<p class="text-slate-500 text-xs py-2">No exportable ZFS pools found</p>';
        } else {
            data.pools.forEach(pool => {
                listDiv.innerHTML += `
                    <div class="flex justify-between items-center p-3 bg-slate-900/40 border border-slate-800 rounded-xl">
                        <span class="font-bold text-white font-mono text-sm">${pool.name}</span>
                        <button onclick="importPool('${pool.name}')" class="bg-cyan-500 hover:bg-cyan-600 text-slate-950 text-xs font-bold px-3 py-1.5 rounded-lg transition-all">
                            Import
                        </button>
                    </div>
                `;
            });
        }
    } catch (e) {
        console.error('Error fetching importable pools:', e);
    }
    document.getElementById('import-pool-modal').classList.remove('hidden');
}

function hideImportPool() {
    document.getElementById('import-pool-modal').classList.add('hidden');
}

async function importPool(name) {
    try {
        const response = await fetch(`${API_BASE}/zfs/pools/import`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({name})
        });
        
        if (response.ok) {
            alert(`ZFS Pool "${name}" successfully imported!`);
            hideImportPool();
            loadZFSPools();
            loadZfsDatasets();
            loadZfsDevices();
        } else {
            alert('Failed to import pool');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// Shares Listing
async function loadShares() {
    try {
        // Load SMB shares
        const smbResp = await fetch(`${API_BASE}/shares/smb`);
        const smbData = await smbResp.json();
        
        const smbList = document.getElementById('smb-list');
        smbList.innerHTML = '';
        
        if (smbData.shares.length === 0) {
            smbList.innerHTML = '<p class="text-slate-500 text-xs">No active SMB shares configured</p>';
        } else {
            smbData.shares.forEach(share => {
                const path = (share.config && (share.config.path || share.config.Path)) || 'N/A';
                const comment = (share.config && (share.config.comment || share.config.Comment)) || '';
                const isSysAdmin = share.name.endsWith('$') || share.name === 'IPC$' || share.name === 'ADMIN$';
                
                smbList.innerHTML += `
                    <div class="p-4 bg-slate-900/40 border border-slate-800 rounded-xl flex justify-between items-center">
                        <div>
                            <div class="flex items-center space-x-2">
                                <span class="font-bold text-white text-base">${share.name}</span>
                                ${isSysAdmin ? '<span class="text-[10px] text-cyan-400 bg-cyan-500/10 border border-cyan-500/20 px-2 py-0.5 rounded font-semibold uppercase">System Admin</span>' : ''}
                            </div>
                            <div class="text-xs text-slate-400 font-mono mt-1">${path}</div>
                            ${comment ? `<div class="text-[11px] text-slate-500 mt-0.5">${comment}</div>` : ''}
                        </div>
                        <button onclick="deleteSMB('${share.name}')" class="text-rose-500 hover:text-rose-400 font-bold text-xs bg-rose-500/10 hover:bg-rose-500/20 px-3 py-1.5 rounded-lg border border-rose-500/20 transition-all">
                            Delete
                        </button>
                    </div>
                `;
            });
        }
        
        // Load NFS exports
        const nfsResp = await fetch(`${API_BASE}/shares/nfs`);
        const nfsData = await nfsResp.json();
        
        const nfsList = document.getElementById('nfs-list');
        nfsList.innerHTML = '';
        
        if (nfsData.exports.length === 0) {
            nfsList.innerHTML = '<p class="text-slate-500 text-xs">No active NFS exports configured</p>';
        } else {
            nfsData.exports.forEach(exp => {
                nfsList.innerHTML += `
                    <div class="p-4 bg-slate-900/40 border border-slate-800 rounded-xl flex justify-between items-center">
                        <div>
                            <div class="font-bold text-white text-base font-mono">${exp.path}</div>
                            <div class="text-xs text-slate-450 mt-1">Allowed: ${exp.clients.join(', ')}</div>
                        </div>
                        <button onclick="deleteNFS('${exp.path}')" class="text-rose-500 hover:text-rose-400 font-bold text-xs bg-rose-500/10 hover:bg-rose-500/20 px-3 py-1.5 rounded-lg border border-rose-500/20 transition-all">
                            Delete
                        </button>
                    </div>
                `;
            });
        }
    } catch (error) {
        console.error('Shares load error:', error);
    }
}

// SMB actions
function showCreateSMB() {
    document.getElementById('create-smb-modal').classList.remove('hidden');
}

function hideCreateSMB() {
    document.getElementById('create-smb-modal').classList.add('hidden');
    document.getElementById('smb-name').value = '';
    document.getElementById('smb-path').value = '';
}

async function createSMB(event) {
    event.preventDefault();
    const name = document.getElementById('smb-name').value;
    const path = document.getElementById('smb-path').value;
    const readonly = document.getElementById('smb-readonly').checked;
    const guestok = document.getElementById('smb-guestok').checked;
    
    try {
        const response = await fetch(`${API_BASE}/shares/smb`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({name, path, readOnly: readonly, guestOk: guestok})
        });
        
        if (response.ok) {
            alert('SMB share created successfully!');
            hideCreateSMB();
            loadShares();
        } else {
            const err = await response.json();
            alert(`Failed to create share: ${err.error || err.detail || 'Unknown error'}`);
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function deleteSMB(name) {
    if (!confirm(`Are you sure you want to delete the SMB share "${name}"?`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/shares/smb/${name}`, {
            method: 'DELETE'
        });
        
        if (response.ok) {
            alert('SMB share deleted successfully!');
            loadShares();
        } else {
            const err = await response.json();
            alert(`Failed to delete share: ${err.error || 'Unknown error'}`);
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

// NFS actions
function showCreateNFS() {
    document.getElementById('create-nfs-modal').classList.remove('hidden');
}

function hideCreateNFS() {
    document.getElementById('create-nfs-modal').classList.add('hidden');
    document.getElementById('nfs-path').value = '';
    document.getElementById('nfs-clients').value = '';
}

async function createNFS(event) {
    event.preventDefault();
    const path = document.getElementById('nfs-path').value;
    const clientsStr = document.getElementById('nfs-clients').value;
    const options = document.getElementById('nfs-options').value;
    
    const clients = clientsStr.split(',').map(c => c.trim()).filter(c => c.length > 0);
    
    try {
        const response = await fetch(`${API_BASE}/shares/nfs`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({path, clients, options})
        });
        
        if (response.ok) {
            alert('NFS export created successfully!');
            hideCreateNFS();
            loadShares();
        } else {
            const err = await response.json();
            alert(`Failed to export: ${err.error || err.detail || 'Unknown error'}`);
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function deleteNFS(path) {
    if (!confirm(`Are you sure you want to stop exporting "${path}"?`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/shares/nfs`, {
            method: 'DELETE',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({path})
        });
        
        if (response.ok) {
            alert('NFS export removed successfully!');
            loadShares();
        } else {
            const err = await response.json();
            alert(`Failed to remove export: ${err.error || 'Unknown error'}`);
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

// Network Tab
async function loadNetwork() {
    try {
        // Tailscale status
        const tailscaleResp = await fetch(`${API_BASE}/network/tailscale/status`);
        const tailscaleData = await tailscaleResp.json();
        
        const statusDiv = document.getElementById('tailscale-status');
        if (tailscaleData.running) {
            statusDiv.innerHTML = '<span class="text-emerald-400 bg-emerald-500/10 border-emerald-500/20 border text-xs px-2.5 py-0.5 rounded-lg">Connected & Active</span>';
        } else if (tailscaleData.installed) {
            statusDiv.innerHTML = '<span class="text-amber-400 bg-amber-500/10 border-amber-500/20 border text-xs px-2.5 py-0.5 rounded-lg">Installed (Stopped)</span>';
        } else {
            statusDiv.innerHTML = '<span class="text-rose-400 bg-rose-500/10 border-rose-500/20 border text-xs px-2.5 py-0.5 rounded-lg">Not Configured</span>';
        }
        
        // Network interfaces
        const ifacesResp = await fetch(`${API_BASE}/network/interfaces`);
        const ifacesData = await ifacesResp.json();
        
        const ifacesList = document.getElementById('interfaces-list');
        ifacesList.innerHTML = '';
        
        ifacesData.interfaces.forEach(iface => {
            const isUp = iface.state === 'up';
            const stateClass = isUp ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20 border' : 'text-slate-500 bg-slate-500/10 border-slate-500/20 border';
            
            ifacesList.innerHTML += `
                <div class="p-3.5 bg-slate-900/40 border border-slate-850 rounded-xl space-y-1.5">
                    <div class="flex justify-between items-center">
                        <span class="font-bold text-slate-200 font-mono text-sm">${iface.name}</span>
                        <span class="text-[10px] uppercase font-bold px-2 py-0.5 rounded-lg ${stateClass}">${iface.state}</span>
                    </div>
                    <div class="text-[11px] text-slate-400 font-mono space-y-0.5">
                        ${iface.addresses.map(a => `<span class="block">${a.address}</span>`).join('') || '<span class="text-slate-600">[No IP Assigned]</span>'}
                    </div>
                </div>
            `;
        });
    } catch (error) {
        console.error('Network load error:', error);
    }
}

async function startTailscale() {
    try {
        const response = await fetch(`${API_BASE}/network/tailscale/up`, {method: 'POST'});
        if (response.ok) {
            alert('Tailscale service daemon started!');
            loadNetwork();
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function stopTailscale() {
    try {
        const response = await fetch(`${API_BASE}/network/tailscale/down`, {method: 'POST'});
        if (response.ok) {
            alert('Tailscale service daemon stopped');
            loadNetwork();
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

// UFW Firewall status checking
async function loadFirewallStatus() {
    try {
        const response = await fetch(`${API_BASE}/network/firewall`);
        const data = await response.json();
        
        const statusDiv = document.getElementById('firewall-status');
        const toggleSwitch = document.getElementById('firewall-toggle');
        
        if (data.active) {
            statusDiv.innerHTML = '<span class="text-emerald-400 bg-emerald-500/10 border-emerald-500/20 border text-xs px-2.5 py-0.5 rounded-lg">Firewall Active</span>';
            toggleSwitch.checked = true;
        } else {
            statusDiv.innerHTML = '<span class="text-rose-400 bg-rose-500/10 border-rose-500/20 border text-xs px-2.5 py-0.5 rounded-lg">Firewall Inactive (Vulnerable)</span>';
            toggleSwitch.checked = false;
        }

        // Fetch client IP address
        const ipResp = await fetch(`${API_BASE}/network/myip`);
        const ipData = await ipResp.json();
        document.getElementById('detected-ip').textContent = ipData.ip;
    } catch (error) {
        console.error('Firewall status load error:', error);
    }
}

async function toggleFirewall(element) {
    const enable = element.checked;
    try {
        const response = await fetch(`${API_BASE}/network/firewall/toggle`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({enable})
        });
        
        if (response.ok) {
            alert(enable ? 'UFW Firewall enabled successfully!' : 'UFW Firewall disabled!');
            loadFirewallStatus();
        } else {
            alert('Failed to toggle firewall state');
            loadFirewallStatus();
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
        loadFirewallStatus();
    }
}

async function whitelistCurrentIP() {
    const ip = document.getElementById('detected-ip').textContent;
    if (!ip || ip === 'Detecting...' || ip === 'unknown') {
        alert('Could not detect a valid connection IP');
        return;
    }

    if (!confirm(`Are you sure you want to whitelist your current IP address ${ip} for ALL services and ports?`)) return;

    try {
        const response = await fetch(`${API_BASE}/network/firewall/whitelist`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({ip: ip, port: 0, comment: 'Home Network Whitelist'})
        });
        
        if (response.ok) {
            alert(`IP address ${ip} successfully whitelisted in UFW!`);
            loadFirewallStatus();
        } else {
            alert('Failed to whitelist IP address');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function addCustomWhitelist(event) {
    event.preventDefault();
    const ip = document.getElementById('whitelist-ip').value;
    const port = parseInt(document.getElementById('whitelist-port').value);
    const comment = document.getElementById('whitelist-comment').value;

    try {
        const response = await fetch(`${API_BASE}/network/firewall/whitelist`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({ip, port, comment})
        });
        
        if (response.ok) {
            alert(`IP ${ip} whitelisted successfully!`);
            document.getElementById('whitelist-ip').value = '';
            document.getElementById('whitelist-comment').value = '';
            loadFirewallStatus();
        } else {
            alert('Failed to whitelist custom IP rule');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// User Management Actions
async function loadUsers() {
    try {
        const response = await fetch(`${API_BASE}/users`);
        const data = await response.json();
        
        const listDiv = document.getElementById('users-list');
        listDiv.innerHTML = '';
        
        if (data.users.length === 0) {
            listDiv.innerHTML = '<p class="text-slate-500 text-sm py-2 col-span-2">No active NAS user accounts configured</p>';
            return;
        }
        
        data.users.forEach(user => {
            const role = user.role || 'Operator';
            const roleColor = role === 'Admin' 
                ? 'text-purple-400 bg-purple-500/10 border-purple-500/20' 
                : (role === 'Operator' ? 'text-cyan-400 bg-cyan-500/10 border-cyan-500/20' : 'text-slate-400 bg-slate-500/10 border-slate-500/20');

            listDiv.innerHTML += `
                <div class="glass-card bg-slate-900/40 p-4 rounded-xl border border-slate-850 flex justify-between items-center">
                    <div class="flex items-center space-x-3">
                        <div class="p-2 bg-slate-950/40 rounded-lg text-cyan-400">
                            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"></path>
                            </svg>
                        </div>
                        <div>
                            <div class="flex items-center space-x-2">
                                <span class="block font-bold text-white text-base">${user.username}</span>
                                <span class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded border ${roleColor}">${role}</span>
                            </div>
                            <span class="block text-slate-455 text-[10px]">Samba & Web NAS User</span>
                        </div>
                    </div>
                    <div class="flex items-center space-x-2">
                        <button onclick="showPasswordModalForUser('${user.username}')" class="text-cyan-400 hover:text-cyan-300 font-bold text-xs bg-cyan-500/10 hover:bg-cyan-500/20 px-2.5 py-1.5 rounded-lg border border-cyan-500/20 transition-all flex items-center space-x-1" title="Change or Reset Password">
                            <span>🔑 Password</span>
                        </button>
                        ${user.username !== 'admin' ? `
                        <button onclick="deleteUser('${user.username}')" class="text-rose-500 hover:text-rose-400 font-bold text-xs bg-rose-500/10 hover:bg-rose-500/20 px-2.5 py-1.5 rounded-lg border border-rose-500/20 transition-all">
                            Remove
                        </button>` : `<span class="text-xs text-slate-500 font-mono px-1">Protected</span>`}
                    </div>
                </div>
            `;
        });
    } catch (e) {
        console.error('Error loading users:', e);
    }
}

function showCreateUser() {
    document.getElementById('create-user-modal').classList.remove('hidden');
}

function hideCreateUser() {
    document.getElementById('create-user-modal').classList.add('hidden');
    document.getElementById('user-username').value = '';
    document.getElementById('user-password').value = '';
}

async function createUser(event) {
    event.preventDefault();
    const username = document.getElementById('user-username').value;
    const password = document.getElementById('user-password').value;
    const role = document.getElementById('user-role') ? document.getElementById('user-role').value : 'Operator';
    
    try {
        const response = await fetch(`${API_BASE}/users`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({username, password, role})
        });
        
        if (response.ok) {
            alert(`User "${username}" (${role}) created successfully!`);
            hideCreateUser();
            loadUsers();
        } else {
            const err = await response.json();
            alert(`Failed to create user: ${err.error || 'Unknown error'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function deleteUser(username) {
    if (!confirm(`Are you sure you want to completely delete the NAS user "${username}"?`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/users/${username}`, {
            method: 'DELETE'
        });
        
        if (response.ok) {
            alert(`User "${username}" successfully deleted!`);
            loadUsers();
        } else {
            alert('Failed to delete user');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// ==================== PASSWORD MANAGEMENT ====================
function showPasswordModalForUser(targetUsername) {
    const currentUsername = document.getElementById('header-username')?.textContent?.trim() || 'admin';
    if (targetUsername.toLowerCase() === currentUsername.toLowerCase()) {
        showChangeOwnPassword();
    } else {
        showAdminResetPassword(targetUsername);
    }
}

function showChangeOwnPassword() {
    const currentUsername = document.getElementById('header-username')?.textContent?.trim() || 'admin';
    const targetEl = document.getElementById('pwd-target-user');
    if (targetEl) targetEl.value = currentUsername;
    const modeEl = document.getElementById('pwd-mode');
    if (modeEl) modeEl.value = 'self';
    const headingEl = document.getElementById('pwd-modal-heading');
    if (headingEl) headingEl.textContent = `Change Password (${currentUsername})`;
    
    const currContainer = document.getElementById('pwd-current-container');
    if (currContainer) currContainer.classList.remove('hidden');
    
    const currInput = document.getElementById('pwd-current');
    if (currInput) {
        currInput.required = true;
        currInput.value = '';
    }
    
    const newPwd = document.getElementById('pwd-new');
    if (newPwd) newPwd.value = '';
    const confirmPwd = document.getElementById('pwd-confirm');
    if (confirmPwd) confirmPwd.value = '';
    
    const errDiv = document.getElementById('pwd-error-msg');
    if (errDiv) { errDiv.classList.add('hidden'); errDiv.textContent = ''; }
    
    const modal = document.getElementById('change-password-modal');
    if (modal) {
        modal.classList.remove('hidden');
        modal.classList.add('flex');
    }
}

function showAdminResetPassword(targetUsername) {
    const targetEl = document.getElementById('pwd-target-user');
    if (targetEl) targetEl.value = targetUsername;
    const modeEl = document.getElementById('pwd-mode');
    if (modeEl) modeEl.value = 'admin-reset';
    const headingEl = document.getElementById('pwd-modal-heading');
    if (headingEl) headingEl.textContent = `Reset Password: ${targetUsername}`;
    
    const currContainer = document.getElementById('pwd-current-container');
    if (currContainer) currContainer.classList.add('hidden');
    
    const currInput = document.getElementById('pwd-current');
    if (currInput) {
        currInput.required = false;
        currInput.value = '';
    }
    
    const newPwd = document.getElementById('pwd-new');
    if (newPwd) newPwd.value = '';
    const confirmPwd = document.getElementById('pwd-confirm');
    if (confirmPwd) confirmPwd.value = '';
    
    const errDiv = document.getElementById('pwd-error-msg');
    if (errDiv) { errDiv.classList.add('hidden'); errDiv.textContent = ''; }
    
    const modal = document.getElementById('change-password-modal');
    if (modal) {
        modal.classList.remove('hidden');
        modal.classList.add('flex');
    }
}

function hideChangePassword() {
    const modal = document.getElementById('change-password-modal');
    if (modal) {
        modal.classList.add('hidden');
        modal.classList.remove('flex');
    }
}

async function handlePasswordSubmit(event) {
    event.preventDefault();
    const mode = document.getElementById('pwd-mode').value;
    const targetUser = document.getElementById('pwd-target-user').value;
    const currentPassword = document.getElementById('pwd-current').value;
    const newPassword = document.getElementById('pwd-new').value;
    const confirmPassword = document.getElementById('pwd-confirm').value;
    const errDiv = document.getElementById('pwd-error-msg');
    const submitBtn = document.getElementById('pwd-submit-btn');

    if (newPassword !== confirmPassword) {
        if (errDiv) {
            errDiv.textContent = 'New passwords do not match. Please re-enter.';
            errDiv.classList.remove('hidden');
        }
        return;
    }

    if (newPassword.length < 6) {
        if (errDiv) {
            errDiv.textContent = 'Password must be at least 6 characters long.';
            errDiv.classList.remove('hidden');
        }
        return;
    }

    const originalText = submitBtn.textContent;
    submitBtn.disabled = true;
    submitBtn.textContent = 'Updating Password...';

    try {
        let response;
        if (mode === 'self') {
            response = await fetch(`${API_BASE}/auth/change-password`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ currentPassword, newPassword })
            });
        } else {
            response = await fetch(`${API_BASE}/users/${encodeURIComponent(targetUser)}/password`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ newPassword })
            });
        }

        const data = await response.json();
        if (response.ok) {
            alert(data.message || `Password for ${targetUser} changed successfully!`);
            hideChangePassword();
            loadUsers();
        } else {
            if (errDiv) {
                errDiv.textContent = data.error || 'Failed to update password.';
                errDiv.classList.remove('hidden');
            } else {
                alert(`Error: ${data.error || 'Failed to update password'}`);
            }
        }
    } catch (e) {
        if (errDiv) {
            errDiv.textContent = `Network error: ${e.message}`;
            errDiv.classList.remove('hidden');
        } else {
            alert(`Error: ${e.message}`);
        }
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = originalText;
    }
}


// ZFS Snapshots
async function loadZfsSnapshots() {
    try {
        const response = await fetch(`${API_BASE}/zfs/snapshots`);
        const data = await response.json();
        
        const snapshotsList = document.getElementById('snapshots-list');
        snapshotsList.innerHTML = '';
        
        if (data.snapshots.length === 0) {
            snapshotsList.innerHTML = '<p class="text-slate-500 text-sm">No snapshots taken yet</p>';
            return;
        }
        
        data.snapshots.forEach(snap => {
            snapshotsList.innerHTML += `
                <div class="p-3 bg-slate-900/40 border border-slate-850 rounded-xl flex justify-between items-center text-xs">
                    <div>
                        <span class="block font-bold text-slate-200 font-mono">${snap.name}</span>
                        <span class="block text-slate-450 text-[10px] mt-0.5 font-mono">Size Referenced: ${snap.refer}</span>
                    </div>
                    <div class="flex space-x-2">
                        <button onclick="rollbackSnapshot('${snap.name}')" class="bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-450 border border-emerald-500/20 px-2.5 py-1.5 rounded-lg font-bold transition-all">
                            Rollback
                        </button>
                        <button onclick="destroySnapshot('${snap.name}')" class="bg-rose-500/10 hover:bg-rose-500/20 text-rose-500 border border-rose-500/20 px-2.5 py-1.5 rounded-lg font-bold transition-all">
                            Delete
                        </button>
                    </div>
                </div>
            `;
        });
    } catch (e) {
        console.error('Error loading snapshots:', e);
    }
}

// Create Dataset actions
async function showCreateDataset() {
    try {
        const response = await fetch(`${API_BASE}/zfs/pools`);
        const data = await response.json();
        
        const select = document.getElementById('dataset-pool');
        select.innerHTML = '';
        
        if (data.pools.length === 0) {
            select.innerHTML = '<option value="">-- No pools available --</option>';
        } else {
            data.pools.forEach(pool => {
                select.innerHTML += `<option value="${pool.name}">${pool.name}</option>`;
            });
        }
    } catch (e) {
        console.error('Error fetching pools for dataset:', e);
    }
    document.getElementById('create-dataset-modal').classList.remove('hidden');
}

function hideCreateDataset() {
    document.getElementById('create-dataset-modal').classList.add('hidden');
    document.getElementById('dataset-name').value = '';
}

async function createDataset(event) {
    event.preventDefault();
    const pool = document.getElementById('dataset-pool').value;
    const name = document.getElementById('dataset-name').value;
    
    if (!pool) {
        alert('Please select a valid parent pool.');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/zfs/datasets`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({pool, name})
        });
        
        if (response.ok) {
            alert(`Dataset "${pool}/${name}" created successfully!`);
            hideCreateDataset();
            loadZfsDatasets();
        } else {
            const err = await response.json();
            alert(`Failed to create dataset: ${err.error || 'Unknown error'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function deleteDataset(name) {
    if (!confirm(`Are you sure you want to delete dataset "${name}"?\nWARNING: This will permanently delete all files inside the dataset!`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/zfs/datasets/${name}`, {
            method: 'DELETE'
        });
        
        if (response.ok) {
            alert(`Dataset "${name}" successfully deleted!`);
            loadZfsDatasets();
            loadZfsSnapshots();
        } else {
            alert('Failed to delete dataset');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// Snapshot actions
async function showCreateSnapshot() {
    try {
        // Load datasets to populate dropdown
        const response = await fetch(`${API_BASE}/zfs/datasets`);
        const data = await response.json();
        
        const select = document.getElementById('snapshot-dataset');
        select.innerHTML = '';
        
        if (data.datasets.length === 0) {
            select.innerHTML = '<option value="">-- No datasets available --</option>';
        } else {
            data.datasets.forEach(ds => {
                select.innerHTML += `<option value="${ds.name}">${ds.name}</option>`;
            });
        }
    } catch (e) {
        console.error('Error fetching datasets for snapshots:', e);
    }
    document.getElementById('create-snapshot-modal').classList.remove('hidden');
}

function hideCreateSnapshot() {
    document.getElementById('create-snapshot-modal').classList.add('hidden');
    document.getElementById('snapshot-name').value = '';
}

async function createSnapshot(event) {
    event.preventDefault();
    const dataset = document.getElementById('snapshot-dataset').value;
    const name = document.getElementById('snapshot-name').value;
    
    if (!dataset) {
        alert('Please select a valid target dataset.');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/zfs/snapshots`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({dataset, name})
        });
        
        if (response.ok) {
            alert(`Snapshot taken successfully: ${dataset}@${name}`);
            hideCreateSnapshot();
            loadZfsSnapshots();
        } else {
            const err = await response.json();
            alert(`Failed to take snapshot: ${err.error || 'Unknown error'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function rollbackSnapshot(snapName) {
    if (!confirm(`Are you sure you want to rollback to snapshot "${snapName}"?\nWARNING: Any changes made after this snapshot was taken will be permanently lost!`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/zfs/snapshots/rollback`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({snapshot: snapName})
        });
        
        if (response.ok) {
            alert(`Rollback completed successfully!`);
            loadZfsDatasets();
            loadZfsSnapshots();
        } else {
            alert('Failed to rollback snapshot');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function destroySnapshot(snapName) {
    if (!confirm(`Are you sure you want to delete snapshot "${snapName}"?`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/zfs/snapshots/${snapName}`, {
            method: 'DELETE'
        });
        
        if (response.ok) {
            alert(`Snapshot deleted successfully.`);
            loadZfsSnapshots();
        } else {
            alert('Failed to delete snapshot');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// Pool scrub / destroy actions
async function scrubPool(poolName) {
    if (!confirm(`Are you sure you want to initiate a ZFS scrub on pool "${poolName}"?\nThis scans all data blocks for integrity errors and repairs them in the background.`)) return;
    
    try {
        const response = await fetch(`${API_BASE}/zfs/pools/scrub`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({pool: poolName})
        });
        
        if (response.ok) {
            alert(`Pool scrub initiated successfully on "${poolName}"! You can monitor the health status in the pool view.`);
            loadZFSPools();
        } else {
            alert('Failed to initiate pool scrub');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function destroyPool(poolName) {
    if (poolName.startsWith('Windows_') || poolName.startsWith('Drive_')) {
        alert(`Host volume "${poolName}" is a physical Windows drive and cannot be deleted.`);
        return;
    }
    const doubleCheck = confirm(`DANGER WARNING: Are you sure you want to completely destroy the ZFS pool "${poolName}"?\n\nThis will PERMANENTLY DESTROY all datasets, snapshots, and data files within this pool! THIS CANNOT BE UNDONE!`);
    if (!doubleCheck) return;
    
    const finalConfirmation = prompt(`To confirm destroying the pool, please type the pool name exactly ("${poolName}"):`);
    if (finalConfirmation !== poolName) {
        alert('Pool name mismatch. Destroy operation cancelled.');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/zfs/pools/${poolName}`, {
            method: 'DELETE'
        });
        
        if (response.ok) {
            alert(`Pool "${poolName}" has been completely destroyed.`);
            loadZFSPools();
            loadZfsDatasets();
            loadZfsSnapshots();
            loadZfsDevices();
        } else {
            alert('Failed to destroy pool. Make sure there are no active share mounts or processes using it.');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// Authentication Flow
async function checkAuth() {
    try {
        const response = await fetch(`${API_BASE}/auth/status`);
        const data = await response.json();
        
        if (data.authenticated) {
            document.getElementById('login-screen').classList.add('hidden');
            
            // Update active user badge in header
            const userEl = document.getElementById('header-username');
            const roleEl = document.getElementById('header-user-role');
            if (userEl && data.username) userEl.textContent = data.username;
            if (roleEl && data.role) {
                roleEl.textContent = data.role;
                if (data.role === 'Admin') {
                    roleEl.className = 'px-2 py-0.5 text-[10px] uppercase font-bold tracking-wider rounded-md bg-purple-500/20 text-purple-400 border border-purple-500/30';
                } else if (data.role === 'Operator') {
                    roleEl.className = 'px-2 py-0.5 text-[10px] uppercase font-bold tracking-wider rounded-md bg-cyan-500/20 text-cyan-400 border border-cyan-500/30';
                } else {
                    roleEl.className = 'px-2 py-0.5 text-[10px] uppercase font-bold tracking-wider rounded-md bg-amber-500/20 text-amber-400 border border-amber-500/30';
                }
            }

            // Sync quick indicators
            updateHeaderQuickIndicators();
            showTab('dashboard');
        } else {
            document.getElementById('login-screen').classList.remove('hidden');
        }
    } catch (e) {
        console.error('Auth check error:', e);
        document.getElementById('login-screen').classList.remove('hidden');
    }
}

async function handleLogin(event) {
    event.preventDefault();
    const username = document.getElementById('login-username').value;
    const password = document.getElementById('login-password').value;
    const errDiv = document.getElementById('login-error');
    
    errDiv.classList.add('hidden');
    
    try {
        const response = await fetch(`${API_BASE}/auth/login`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({username, password})
        });
        
        if (response.ok) {
            document.getElementById('login-screen').classList.add('hidden');
            document.getElementById('login-username').value = '';
            document.getElementById('login-password').value = '';
            await checkAuth();
        } else {
            errDiv.classList.remove('hidden');
        }
    } catch (e) {
        alert(`Login error: ${e.message}`);
    }
}

async function handleLogout() {
    if (!confirm('Are you sure you want to sign out of the dashboard?')) return;
    
    try {
        const response = await fetch(`${API_BASE}/auth/logout`, {
            method: 'POST'
        });
        
        if (response.ok) {
            document.getElementById('login-screen').classList.remove('hidden');
        }
    } catch (e) {
        console.error('Logout error:', e);
    }
}

// Initialize - check auth first
document.addEventListener('DOMContentLoaded', async () => {
    await checkAuth();
    initSignalR();
    
    // Auto-refresh fallback every 10 seconds if SignalR is idle
    setInterval(() => {
        const loginScreen = document.getElementById('login-screen');
        const dashboardTab = document.getElementById('tab-dashboard');
        if (loginScreen && loginScreen.classList.contains('hidden') && dashboardTab && !dashboardTab.classList.contains('hidden')) {
            if (!nasHubConnection || nasHubConnection.state !== 'Connected') {
                loadDashboard();
            }
        }
    }, 10000);
});

// Cloud Storage Manager
async function loadCloud() {
    try {
        const response = await fetch(`${API_BASE}/cloud/status`);
        const data = await response.json();
        
        const rcloneStatus = document.getElementById('rclone-status');
        if (data.rcloneMounted) {
            rcloneStatus.className = "font-bold text-emerald-400 bg-emerald-500/10 px-2.5 py-0.5 rounded-full border border-emerald-500/20 text-xs";
            rcloneStatus.textContent = "Active";
        } else {
            rcloneStatus.className = "font-bold text-rose-500 bg-rose-500/10 px-2.5 py-0.5 rounded-full border border-rose-500/20 text-xs";
            rcloneStatus.textContent = "Inactive";
        }
        
        const mergerfsStatus = document.getElementById('mergerfs-status');
        if (data.mergerfsMounted) {
            mergerfsStatus.className = "font-bold text-emerald-400 bg-emerald-500/10 px-2.5 py-0.5 rounded-full border border-emerald-500/20 text-xs";
            mergerfsStatus.textContent = "Active";
        } else {
            mergerfsStatus.className = "font-bold text-rose-500 bg-rose-500/10 px-2.5 py-0.5 rounded-full border border-rose-500/20 text-xs";
            mergerfsStatus.textContent = "Inactive";
        }

        const syncStatus = document.getElementById('sync-status');
        if (data.syncActive) {
            syncStatus.className = "font-bold text-purple-400 animate-pulse text-xs";
            syncStatus.textContent = "Syncing Files in Background...";
        } else {
            syncStatus.className = "font-bold text-slate-500 text-xs";
            syncStatus.textContent = "Idle (Standby)";
        }
    } catch (error) {
        console.error('Cloud load error:', error);
    }
}

async function mountCloud() {
    try {
        const response = await fetch(`${API_BASE}/cloud/mount`, { method: 'POST' });
        const res = await response.json();
        if (response.ok) {
            alert('Cloud mount triggered successfully.');
        } else {
            alert(`Error: ${res.error || 'Failed to mount cloud storage'}`);
        }
        loadCloud();
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function unmountCloud() {
    try {
        const response = await fetch(`${API_BASE}/cloud/unmount`, { method: 'POST' });
        const res = await response.json();
        if (response.ok) {
            alert('Cloud unmount triggered successfully.');
        } else {
            alert(`Error: ${res.error || 'Failed to unmount cloud storage'}`);
        }
        loadCloud();
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function mountUnion() {
    try {
        const response = await fetch(`${API_BASE}/cloud/union`, { method: 'POST' });
        const res = await response.json();
        if (response.ok) {
            alert('Union pool mounted successfully.');
        } else {
            alert(`Error: ${res.error || 'Failed to mount union pool'}`);
        }
        loadCloud();
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function unmountUnion() {
    try {
        const response = await fetch(`${API_BASE}/cloud/unmount-union`, { method: 'POST' });
        const res = await response.json();
        if (response.ok) {
            alert('Union pool unmounted successfully.');
        } else {
            alert(`Error: ${res.error || 'Failed to unmount union pool'}`);
        }
        loadCloud();
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

async function runSync() {
    try {
        const response = await fetch(`${API_BASE}/cloud/sync`, { method: 'POST' });
        if (response.ok) {
            alert('Background backup sync task started!');
        } else {
            alert('Failed to trigger background sync task.');
        }
        loadCloud();
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

// ==================== SSL / TLS & LET'S ENCRYPT ====================
async function loadSslStatus() {
    try {
        const response = await fetch(`${API_BASE}/ssl/status`);
        const data = await response.json();

        const badge = document.getElementById('ssl-status-badge');
        if (badge) {
            if (data.enabled) {
                badge.innerHTML = `<span class="text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded border border-emerald-500/20">Active (${data.certType})</span>`;
            } else {
                badge.innerHTML = `<span class="text-rose-400 bg-rose-500/10 px-2 py-0.5 rounded border border-rose-500/20">Disabled (HTTP)</span>`;
            }
        }

        const domainEl = document.getElementById('ssl-domain');
        if (domainEl) domainEl.textContent = data.domain || 'None';

        const expiresEl = document.getElementById('ssl-expires');
        if (expiresEl) {
            expiresEl.textContent = data.expiresAt ? `${data.expiresAt} (${data.daysRemaining}d left)` : 'N/A';
        }

        const portEl = document.getElementById('ssl-port');
        if (portEl) portEl.textContent = data.httpsPort || 8443;
    } catch (e) {
        console.error('Failed to load SSL status:', e);
    }
}

async function requestLetsEncrypt(event) {
    event.preventDefault();
    const domain = document.getElementById('le-domain').value.trim();
    const email = document.getElementById('le-email').value.trim();
    const staging = document.getElementById('le-staging').checked;
    const btn = document.getElementById('le-submit-btn');

    const originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = 'Requesting ACME Challenge... (Please wait)';

    try {
        const response = await fetch(`${API_BASE}/ssl/letsencrypt/request`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ domain, email, staging })
        });
        const res = await response.json();

        if (response.ok) {
            alert(`Success! ${res.message}`);
            loadSslStatus();
        } else {
            alert(`Let's Encrypt Error: ${res.error || 'Challenge or validation failed'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    } finally {
        btn.disabled = false;
        btn.textContent = originalText;
    }
}

async function generateSelfSigned(event) {
    event.preventDefault();
    const domain = document.getElementById('self-domain').value.trim();
    const btn = document.getElementById('self-submit-btn');

    btn.disabled = true;
    btn.textContent = 'Generating RSA Key & Cert...';

    try {
        const response = await fetch(`${API_BASE}/ssl/self-signed`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ domain })
        });
        const res = await response.json();

        if (response.ok) {
            alert(`Success! ${res.message}`);
            loadSslStatus();
        } else {
            alert(`Error: ${res.error || 'Failed to generate self-signed certificate'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    } finally {
        btn.disabled = false;
        btn.textContent = 'Generate Self-Signed Certificate';
    }
}

// ==================== AUTOMATED SNAPSHOT SCHEDULER ====================
async function loadSnapshotSchedule() {
    try {
        const response = await fetch(`${API_BASE}/snapshots/schedule`);
        const data = await response.json();

        const statusEl = document.getElementById('sched-status');
        if (statusEl) {
            statusEl.innerHTML = data.enabled 
                ? '<span class="text-emerald-400">Active</span>' 
                : '<span class="text-slate-400">Disabled</span>';
        }

        const freqEl = document.getElementById('sched-freq');
        if (freqEl) freqEl.textContent = (data.frequency || 'daily').toUpperCase();

        const retEl = document.getElementById('sched-retention');
        if (retEl) retEl.textContent = `${data.retentionCount} Snapshots`;

        const nextEl = document.getElementById('sched-next');
        if (nextEl) {
            nextEl.textContent = data.enabled && data.nextRun 
                ? new Date(data.nextRun).toLocaleString() 
                : (data.enabled ? 'Calculating next run...' : 'Disabled');
        }

        const enableInput = document.getElementById('sched-enable');
        if (enableInput) enableInput.checked = data.enabled;

        const freqInput = document.getElementById('sched-freq-input');
        if (freqInput) freqInput.value = data.frequency || 'daily';

        const retInput = document.getElementById('sched-retention-input');
        if (retInput) retInput.value = data.retentionCount || 7;
    } catch (e) {
        console.error('Failed to load snapshot schedule:', e);
    }
}

async function saveSnapshotSchedule(event) {
    event.preventDefault();
    const enabled = document.getElementById('sched-enable').checked;
    const frequency = document.getElementById('sched-freq-input').value;
    const retentionCount = parseInt(document.getElementById('sched-retention-input').value) || 7;

    try {
        const response = await fetch(`${API_BASE}/snapshots/schedule`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                enabled,
                frequency,
                targetPool: 'tank',
                retentionCount
            })
        });

        if (response.ok) {
            alert('Snapshot schedule saved successfully!');
            loadSnapshotSchedule();
        } else {
            alert('Failed to save snapshot schedule.');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function runSnapshotNow() {
    try {
        const response = await fetch(`${API_BASE}/snapshots/schedule/run`, { method: 'POST' });
        if (response.ok) {
            alert('Automated snapshot cycle executed successfully!');
            loadZfsSnapshots();
            loadSnapshotSchedule();
        } else {
            alert('Failed to trigger snapshot cycle.');
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// ==================== SYSTEM & EXTENSIONS QUICK SYNC ====================
async function updateHeaderQuickIndicators() {
    try {
        // Quick SSL check
        const sslResp = await fetch(`${API_BASE}/ssl/status`);
        if (sslResp.ok) {
            const sslData = await sslResp.json();
            const sslText = document.getElementById('header-ssl-text');
            const dashSslBadge = document.getElementById('dash-ssl-badge');
            const dashSslSub = document.getElementById('dash-ssl-sub');
            if (sslData.enabled) {
                if (sslText) sslText.textContent = `HTTPS :${sslData.httpsPort || 8443}`;
                if (dashSslBadge) {
                    dashSslBadge.className = 'px-2 py-0.5 text-[10px] font-bold rounded-md bg-emerald-500/10 text-emerald-400 border border-emerald-500/20';
                    dashSslBadge.textContent = 'Active :8443';
                }
                if (dashSslSub) dashSslSub.textContent = `${sslData.certType} (${sslData.daysRemaining}d left)`;
            } else {
                if (sslText) sslText.textContent = 'HTTP (No SSL)';
                if (dashSslBadge) {
                    dashSslBadge.className = 'px-2 py-0.5 text-[10px] font-bold rounded-md bg-slate-800 text-slate-400 border border-slate-700';
                    dashSslBadge.textContent = 'Disabled';
                }
                if (dashSslSub) dashSslSub.textContent = 'Click to configure';
            }
        }

        // Quick snapshot schedule check
        const snapResp = await fetch(`${API_BASE}/snapshots/schedule`);
        if (snapResp.ok) {
            const snapData = await snapResp.json();
            const dashSnapBadge = document.getElementById('dash-snap-badge');
            const dashSnapSub = document.getElementById('dash-snap-sub');
            if (snapData.enabled) {
                if (dashSnapBadge) {
                    dashSnapBadge.className = 'px-2 py-0.5 text-[10px] font-bold rounded-md bg-cyan-500/10 text-cyan-400 border border-cyan-500/20';
                    dashSnapBadge.textContent = `${(snapData.frequency || 'Daily').toUpperCase()}`;
                }
                if (dashSnapSub) dashSnapSub.textContent = `Retaining ${snapData.retentionCount} snapshots`;
            } else {
                if (dashSnapBadge) {
                    dashSnapBadge.className = 'px-2 py-0.5 text-[10px] font-bold rounded-md bg-slate-800 text-slate-400 border border-slate-700';
                    dashSnapBadge.textContent = 'Off';
                }
                if (dashSnapSub) dashSnapSub.textContent = 'Automated backup paused';
            }
        }

        // Quick plugins count
        const pluginsResp = await fetch(`${API_BASE}/plugins`);
        if (pluginsResp.ok) {
            const pData = await pluginsResp.json();
            const activeCount = (pData.plugins || []).filter(p => p.enabled).length;
            const countNav = document.getElementById('plugins-nav-count');
            const dashPluginBadge = document.getElementById('dash-plugin-badge');
            const dashPluginSub = document.getElementById('dash-plugin-sub');
            if (countNav) countNav.textContent = activeCount;
            if (dashPluginBadge) dashPluginBadge.textContent = `${activeCount} Active`;
            if (dashPluginSub) dashPluginSub.textContent = `${pData.plugins.length} Available in Catalog`;
        }
    } catch (e) {
        console.error('Failed to update quick indicators:', e);
    }
}

// ==================== PLUGINS & EXTENSION CENTER ====================
let allPlugins = [];
let activePluginCategory = 'all';
let pluginSearchQuery = '';

async function loadPlugins() {
    try {
        const response = await fetch(`${API_BASE}/plugins`);
        const data = await response.json();
        allPlugins = data.plugins || [];

        // Update stats
        const activeCount = allPlugins.filter(p => p.enabled).length;
        const activeStat = document.getElementById('plugin-stat-active');
        if (activeStat) activeStat.textContent = `${activeCount} Running`;

        const catalogStat = document.getElementById('plugin-stat-catalog');
        if (catalogStat) catalogStat.textContent = `${allPlugins.length} Available`;

        const navCount = document.getElementById('plugins-nav-count');
        if (navCount) navCount.textContent = activeCount;

        renderPlugins();
    } catch (e) {
        console.error('Failed to load plugins:', e);
    }
}

function refreshPlugins() {
    loadPlugins();
}

function filterPlugins(category) {
    activePluginCategory = category;
    
    // Update button styles
    document.querySelectorAll('.plugin-filter-btn').forEach(btn => {
        if (btn.getAttribute('data-category') === category) {
            btn.className = 'plugin-filter-btn px-3 py-1.5 rounded-lg text-xs font-semibold bg-cyan-500/20 text-cyan-400 border border-cyan-500/40';
        } else {
            btn.className = 'plugin-filter-btn px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-900 text-slate-400 hover:text-white border border-slate-800';
        }
    });

    renderPlugins();
}

function searchPlugins(event) {
    pluginSearchQuery = (event.target.value || '').toLowerCase().trim();
    renderPlugins();
}

function renderPlugins() {
    const grid = document.getElementById('plugins-grid');
    if (!grid) return;

    let filtered = allPlugins;

    // Filter by Category
    if (activePluginCategory === 'installed') {
        filtered = filtered.filter(p => p.installed);
    } else if (activePluginCategory !== 'all') {
        filtered = filtered.filter(p => p.category.toLowerCase() === activePluginCategory.toLowerCase());
    }

    // Filter by Search Query
    if (pluginSearchQuery) {
        filtered = filtered.filter(p => 
            p.name.toLowerCase().includes(pluginSearchQuery) ||
            p.description.toLowerCase().includes(pluginSearchQuery) ||
            p.author.toLowerCase().includes(pluginSearchQuery) ||
            p.id.toLowerCase().includes(pluginSearchQuery)
        );
    }

    if (filtered.length === 0) {
        grid.innerHTML = `
            <div class="col-span-full glass-card rounded-2xl p-12 text-center border border-slate-800 space-y-3">
                <span class="text-4xl">🔍</span>
                <h4 class="text-lg font-bold text-white">No plugins match your filter</h4>
                <p class="text-sm text-slate-400">Try adjusting your search query or selecting a different category.</p>
            </div>
        `;
        return;
    }

    grid.innerHTML = filtered.map(plugin => {
        const isInstalled = plugin.installed;
        const isRunning = plugin.enabled;

        let statusBadge = '';
        if (isRunning) {
            statusBadge = `<span class="inline-flex items-center space-x-1.5 px-2.5 py-0.5 rounded-full text-xs font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                <span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span>
                <span>Active</span>
            </span>`;
        } else if (isInstalled) {
            statusBadge = `<span class="inline-flex items-center space-x-1.5 px-2.5 py-0.5 rounded-full text-xs font-bold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                <span class="w-1.5 h-1.5 rounded-full bg-amber-400"></span>
                <span>Stopped</span>
            </span>`;
        } else {
            statusBadge = `<span class="inline-flex items-center space-x-1.5 px-2.5 py-0.5 rounded-full text-xs font-bold bg-slate-800 text-slate-400 border border-slate-700">
                <span>Available</span>
            </span>`;
        }

        // Web UI link (if active & has defaultPort or webPath)
        const host = window.location.hostname || 'localhost';
        const port = plugin.defaultPort;
        const webUrl = (plugin.webPath !== null && port) ? `http://${host}:${port}${plugin.webPath || '/'}` : null;

        return `
            <div class="glass-card rounded-2xl p-6 shadow-xl flex flex-col justify-between border border-slate-800 hover:border-slate-700 transition-all space-y-5">
                <div class="space-y-4">
                    <div class="flex items-start justify-between">
                        <div class="flex items-center space-x-3">
                            <span class="text-3xl bg-slate-900/80 p-2.5 rounded-2xl border border-slate-800">${plugin.icon}</span>
                            <div>
                                <h4 class="text-base font-bold text-white flex items-center space-x-2">
                                    <span>${plugin.name}</span>
                                </h4>
                                <div class="flex items-center space-x-2 mt-0.5">
                                    <span class="text-[11px] font-mono text-cyan-400">v${plugin.version}</span>
                                    <span class="text-slate-600">•</span>
                                    <span class="text-[11px] text-slate-400">${plugin.category}</span>
                                </div>
                            </div>
                        </div>
                        ${statusBadge}
                    </div>

                    <p class="text-xs text-slate-300 leading-relaxed">${plugin.description}</p>

                    <div class="text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-3 font-mono">
                        <span>By ${plugin.author}</span>
                        ${plugin.defaultPort ? `<span>Port: <span class="text-slate-200 font-bold">${plugin.defaultPort}</span></span>` : ''}
                    </div>
                </div>

                <div class="space-y-2 pt-2 border-t border-slate-800/80">
                    <div class="flex items-center justify-between gap-2">
                        ${isInstalled ? `
                            <div class="flex items-center space-x-2">
                                <label class="relative inline-flex items-center cursor-pointer">
                                    <input type="checkbox" class="sr-only peer" ${isRunning ? 'checked' : ''} onchange="togglePlugin('${plugin.id}', this.checked)">
                                    <div class="w-9 h-5 bg-slate-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-cyan-500"></div>
                                </label>
                                <span class="text-xs font-semibold ${isRunning ? 'text-cyan-400' : 'text-slate-400'}">${isRunning ? 'Enabled' : 'Disabled'}</span>
                            </div>
                            <div class="flex items-center space-x-2">
                                ${isRunning && webUrl ? `
                                    <a href="${webUrl}" target="_blank" class="bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/30 text-xs font-bold px-3 py-1.5 rounded-lg transition-all flex items-center space-x-1" title="Open Web Service UI">
                                        <span>Open UI</span>
                                        <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"></path></svg>
                                    </a>
                                ` : ''}
                                <button onclick="showContainerLogs('${plugin.id}', '${plugin.name}')" class="bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs px-2.5 py-1.5 rounded-lg border border-slate-700 transition-all" title="View Container Logs">
                                    📋
                                </button>
                                <button onclick="openPluginConfig('${plugin.id}')" class="bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs px-2.5 py-1.5 rounded-lg border border-slate-700 transition-all" title="Settings">
                                    ⚙️
                                </button>
                                <button onclick="uninstallPlugin('${plugin.id}')" class="bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 text-xs px-2.5 py-1.5 rounded-lg border border-rose-500/20 transition-all" title="Uninstall Plugin">
                                    🗑️
                                </button>
                            </div>
                        ` : `
                            <button onclick="installPlugin('${plugin.id}')" class="w-full bg-cyan-500 hover:bg-cyan-600 text-slate-950 font-bold text-xs py-2 rounded-xl transition-all shadow-md shadow-cyan-500/10 flex items-center justify-center space-x-1.5">
                                <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"></path></svg>
                                <span>Install Extension</span>
                            </button>
                        `}
                    </div>
                </div>
            </div>
        `;
    }).join('');
}

async function installPlugin(id) {
    try {
        const response = await fetch(`${API_BASE}/plugins/install`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id })
        });
        const data = await response.json();
        if (response.ok) {
            loadPlugins();
            updateHeaderQuickIndicators();
        } else {
            alert(`Install Error: ${data.error || 'Failed to install plugin'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function uninstallPlugin(id) {
    if (!confirm(`Are you sure you want to uninstall this plugin?`)) return;
    try {
        const response = await fetch(`${API_BASE}/plugins/uninstall`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id })
        });
        const data = await response.json();
        if (response.ok) {
            loadPlugins();
            updateHeaderQuickIndicators();
        } else {
            alert(`Uninstall Error: ${data.error || 'Failed to uninstall plugin'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

async function togglePlugin(id, enabled) {
    try {
        const response = await fetch(`${API_BASE}/plugins/toggle`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id, enabled })
        });
        const data = await response.json();
        if (response.ok) {
            loadPlugins();
            updateHeaderQuickIndicators();
        } else {
            alert(`Toggle Error: ${data.error || 'Failed to toggle plugin state'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

function openPluginConfig(id) {
    const plugin = allPlugins.find(p => p.id === id);
    if (!plugin) return;

    document.getElementById('config-plugin-id').value = id;
    document.getElementById('plugin-modal-title').innerHTML = `<span>Settings: ${plugin.name}</span>`;
    document.getElementById('config-plugin-port').value = plugin.defaultPort || '';

    const extraFields = document.getElementById('config-extra-fields');
    extraFields.innerHTML = `
        <div class="bg-slate-900/60 p-3 rounded-xl border border-slate-800 text-xs text-slate-400 space-y-1">
            <p><strong class="text-white">Author:</strong> ${plugin.author}</p>
            <p><strong class="text-white">Version:</strong> ${plugin.version}</p>
            <p><strong class="text-white">Category:</strong> ${plugin.category}</p>
            <p><strong class="text-white">Status:</strong> ${plugin.status}</p>
        </div>
    `;

    document.getElementById('plugin-config-modal').classList.remove('hidden');
}

function hidePluginConfig() {
    document.getElementById('plugin-config-modal').classList.add('hidden');
}

async function savePluginConfig(event) {
    event.preventDefault();
    const id = document.getElementById('config-plugin-id').value;
    const portVal = document.getElementById('config-plugin-port').value;
    const port = portVal ? parseInt(portVal) : null;

    try {
        const response = await fetch(`${API_BASE}/plugins/config`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id, port, settings: {} })
        });
        const data = await response.json();
        if (response.ok) {
            hidePluginConfig();
            loadPlugins();
        } else {
            alert(`Config Error: ${data.error || 'Failed to save configuration'}`);
        }
    } catch (e) {
        alert(`Error: ${e.message}`);
    }
}

// ==================== REAL-TIME SIGNALR TELEMETRY & TOASTS ====================
let nasHubConnection = null;

function initSignalR() {
    if (typeof signalR === 'undefined') {
        console.warn('SignalR library not loaded, skipping real-time socket connection.');
        return;
    }
    
    if (nasHubConnection && nasHubConnection.state === signalR.HubConnectionState.Connected) {
        return;
    }

    nasHubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/nas')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    nasHubConnection.on('ReceiveTelemetry', (telemetry) => {
        if (!telemetry) return;

        // CPU Usage
        if (telemetry.cpu && typeof telemetry.cpu.percent === 'number') {
            const cpuEl = document.getElementById('cpu-usage');
            if (cpuEl) cpuEl.textContent = `${telemetry.cpu.percent.toFixed(0)}%`;
            if (typeof setProgress === 'function') setProgress('cpu', telemetry.cpu.percent);
            const cpuDet = document.getElementById('cpu-core-details');
            if (cpuDet) cpuDet.textContent = `Sys Load Avg: ${telemetry.cpu.percent.toFixed(1)}%`;
        }

        // Memory Usage
        if (telemetry.memory && typeof telemetry.memory.percent === 'number') {
            const memEl = document.getElementById('mem-usage');
            if (memEl) memEl.textContent = `${telemetry.memory.percent.toFixed(0)}%`;
            if (typeof setProgress === 'function') setProgress('mem', telemetry.memory.percent);
            const memDet = document.getElementById('mem-ram-details');
            if (memDet) memDet.textContent = `RAM Allocation: ${telemetry.memory.percent.toFixed(1)}%`;
        }

        // Disk Usage
        if (telemetry.disk && telemetry.disk.percent) {
            const diskPercent = parseFloat(telemetry.disk.percent.replace('%', '')) || 0;
            const diskEl = document.getElementById('disk-usage');
            if (diskEl) diskEl.textContent = `${diskPercent.toFixed(0)}%`;
            if (typeof setProgress === 'function') setProgress('disk', diskPercent);
            const diskDet = document.getElementById('disk-pool-details');
            if (diskDet) diskDet.textContent = `Root Space Used: ${telemetry.disk.percent}`;
        }
    });

    nasHubConnection.on('ReceiveAlert', (alertData) => {
        if (alertData) {
            showToast(alertData.title || 'SimpleNAS Alert', alertData.message || '', alertData.level || 'info');
        }
    });

    nasHubConnection.start()
        .then(() => {
            console.log('Connected to SimpleNAS SignalR Telemetry Hub');
        })
        .catch(err => {
            console.warn('SignalR initial connection failed, will use HTTP polling fallback:', err);
        });
}

function showToast(title, message, level = 'info') {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const toast = document.createElement('div');
    const colors = {
        info: 'bg-slate-900/90 border-cyan-500/40 text-cyan-300',
        success: 'bg-slate-900/90 border-emerald-500/40 text-emerald-300',
        warning: 'bg-slate-900/90 border-amber-500/40 text-amber-300',
        error: 'bg-slate-900/90 border-rose-500/40 text-rose-300',
        security: 'bg-purple-950/90 border-purple-500/40 text-purple-300'
    };
    const icons = {
        info: 'ℹ️',
        success: '✅',
        warning: '⚠️',
        error: '❌',
        security: '🛡️'
    };

    const colorClass = colors[level.toLowerCase()] || colors.info;
    const icon = icons[level.toLowerCase()] || icons.info;

    toast.className = `pointer-events-auto p-3.5 rounded-xl border shadow-2xl backdrop-blur-md flex items-start space-x-3 max-w-sm transition-all duration-300 transform translate-y-2 opacity-0 ${colorClass}`;
    toast.innerHTML = `
        <span class="text-base">${icon}</span>
        <div class="flex-1 text-xs">
            <div class="font-bold text-white mb-0.5">${escapeHtml(title)}</div>
            <div class="text-slate-300 leading-relaxed">${escapeHtml(message)}</div>
        </div>
        <button onclick="this.parentElement.remove()" class="text-slate-400 hover:text-white text-xs">&times;</button>
    `;

    container.appendChild(toast);

    setTimeout(() => {
        toast.classList.remove('translate-y-2', 'opacity-0');
    }, 10);

    setTimeout(() => {
        if (toast.parentElement) {
            toast.classList.add('opacity-0', 'translate-y-2');
            setTimeout(() => toast.remove(), 300);
        }
    }, 5000);
}

// ==================== WEB FILE EXPLORER ====================
window.currentBrowserPath = '';
window.allFileItems = [];

async function loadFileRoots() {
    try {
        const response = await fetch(`${API_BASE}/files/roots`);
        if (!response.ok) return;
        const roots = await response.json();
        const rootsBar = document.getElementById('file-roots-bar');
        if (!rootsBar) return;

        let html = '<span class="text-xs font-mono uppercase text-slate-500 mr-1">Root Storage:</span>';
        roots.forEach(root => {
            const isSelected = window.currentBrowserPath.startsWith(root);
            const activeStyle = isSelected
                ? 'bg-cyan-500/20 text-cyan-400 border-cyan-500/40 font-bold'
                : 'bg-slate-800/80 hover:bg-slate-700 text-slate-300 border-slate-700';
            html += `
                <button onclick="loadFiles('${encodeURIComponent(root)}')" class="px-2.5 py-1 text-xs font-mono rounded-lg border transition-all ${activeStyle}">
                    💾 ${root}
                </button>
            `;
        });
        rootsBar.innerHTML = html;

        if (!window.currentBrowserPath && roots.length > 0) {
            loadFiles(roots[0]);
        }
    } catch (e) {
        console.error('Error loading file roots:', e);
    }
}

async function loadFiles(path = '') {
    const decodedPath = decodeURIComponent(path);
    window.currentBrowserPath = decodedPath;
    const tbody = document.getElementById('files-table-body');
    if (tbody) {
        tbody.innerHTML = '<tr><td colspan="4" class="py-12 text-center text-slate-500 font-sans">Loading directory...</td></tr>';
    }

    try {
        const url = decodedPath ? `${API_BASE}/files/list?path=${encodeURIComponent(decodedPath)}` : `${API_BASE}/files/list`;
        const response = await fetch(url);
        if (!response.ok) {
            const err = await response.json();
            if (tbody) {
                tbody.innerHTML = `<tr><td colspan="4" class="py-8 text-center text-rose-400 font-sans">Failed to load directory: ${err.error || 'Access denied'}</td></tr>`;
            }
            return;
        }

        const data = await response.json();
        window.currentBrowserPath = data.currentPath;
        window.allFileItems = data.items || [];

        renderBreadcrumbs(data.breadcrumbs || [data.currentPath], data.parentPath);
        renderFilesTable(window.allFileItems);
        loadFileRoots();
    } catch (e) {
        if (tbody) {
            tbody.innerHTML = `<tr><td colspan="4" class="py-8 text-center text-rose-400 font-sans">Error: ${e.message}</td></tr>`;
        }
    }
}

function renderBreadcrumbs(breadcrumbs, parentPath) {
    const container = document.getElementById('file-breadcrumbs');
    if (!container) return;

    let html = '';
    if (parentPath) {
        html += `
            <button onclick="loadFiles('${encodeURIComponent(parentPath)}')" class="hover:text-cyan-400 text-slate-400 flex items-center space-x-1" title="Go up to parent directory">
                <span>📁 ..</span>
            </button>
            <span class="text-slate-600">/</span>
        `;
    }

    let accumulatedPath = '';
    breadcrumbs.forEach((segment, idx) => {
        if (idx === 0) {
            accumulatedPath = segment;
        } else {
            const separator = accumulatedPath.includes('\\') ? '\\' : '/';
            accumulatedPath = accumulatedPath.endsWith(separator) ? accumulatedPath + segment : accumulatedPath + separator + segment;
        }
        const isLast = idx === breadcrumbs.length - 1;
        if (isLast) {
            html += `<span class="text-cyan-400 font-bold">${escapeHtml(segment)}</span>`;
        } else {
            html += `
                <button onclick="loadFiles('${encodeURIComponent(accumulatedPath)}')" class="hover:text-cyan-400 text-slate-400 transition-colors">
                    ${escapeHtml(segment)}
                </button>
                <span class="text-slate-600">/</span>
            `;
        }
    });

    container.innerHTML = html;
}

function getFileIcon(item) {
    if (item.isDirectory) return '📁';
    switch (item.typeCategory) {
        case 'image': return '🖼️';
        case 'video': return '🎬';
        case 'audio': return '🎵';
        case 'archive': return '📦';
        case 'code': return '💻';
        case 'document': return '📄';
        default: return '📄';
    }
}

function renderFilesTable(items) {
    const tbody = document.getElementById('files-table-body');
    if (!tbody) return;

    if (!items || items.length === 0) {
        tbody.innerHTML = '<tr><td colspan="4" class="py-12 text-center text-slate-500 font-sans">Directory is empty. Drag and drop files or create a folder.</td></tr>';
        return;
    }

    const sorted = [...items].sort((a, b) => {
        if (a.isDirectory && !b.isDirectory) return -1;
        if (!a.isDirectory && b.isDirectory) return 1;
        return a.name.localeCompare(b.name);
    });

    let html = '';
    sorted.forEach(item => {
        const icon = getFileIcon(item);
        const dateStr = item.lastModified ? new Date(item.lastModified).toLocaleString() : '-';
        const isTextOrCode = ['code', 'document'].includes(item.typeCategory) || ['.txt', '.log', '.json', '.yaml', '.yml', '.conf', '.sh', '.cs', '.js', '.html', '.css', '.md'].some(ext => item.name.toLowerCase().endsWith(ext));
        const isImage = item.typeCategory === 'image';

        const clickAction = item.isDirectory
            ? `loadFiles('${encodeURIComponent(item.fullPath)}')`
            : (isTextOrCode || isImage ? `previewFile('${encodeURIComponent(item.fullPath)}', '${escapeHtml(item.name)}', ${isImage})` : `downloadFile('${encodeURIComponent(item.fullPath)}')`);

        html += `
            <tr class="hover:bg-slate-900/60 transition-colors group">
                <td class="py-3 pl-3">
                    <div class="flex items-center space-x-2.5 cursor-pointer select-none" onclick="${clickAction}">
                        <span class="text-lg">${icon}</span>
                        <span class="font-medium text-slate-200 group-hover:text-cyan-400 transition-colors truncate max-w-xs sm:max-w-md" title="${escapeHtml(item.name)}">
                            ${escapeHtml(item.name)}
                        </span>
                    </div>
                </td>
                <td class="py-3 text-right text-slate-400 font-mono text-xs">
                    ${item.isDirectory ? '<span class="text-slate-600">—</span>' : item.sizeFormatted}
                </td>
                <td class="py-3 pl-6 text-slate-500 text-xs">
                    ${dateStr}
                </td>
                <td class="py-3 pr-3 text-right">
                    <div class="flex items-center justify-end space-x-1">
                        ${!item.isDirectory ? `
                            <button onclick="downloadFile('${encodeURIComponent(item.fullPath)}')" class="p-1.5 rounded-lg bg-slate-800/80 hover:bg-cyan-500/20 text-slate-300 hover:text-cyan-400 border border-slate-700 hover:border-cyan-500/30 transition-all" title="Download File">
                                ⬇️
                            </button>
                        ` : ''}
                        <button onclick="showRenameModal('${encodeURIComponent(item.fullPath)}', '${escapeHtml(item.name)}')" class="p-1.5 rounded-lg bg-slate-800/80 hover:bg-slate-700 text-slate-300 hover:text-white border border-slate-700 transition-all" title="Rename">
                            ✏️
                        </button>
                        <button onclick="deleteFileItem('${encodeURIComponent(item.fullPath)}', '${escapeHtml(item.name)}')" class="p-1.5 rounded-lg bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 border border-rose-500/20 transition-all" title="Delete">
                            🗑️
                        </button>
                    </div>
                </td>
            </tr>
        `;
    });

    tbody.innerHTML = html;
}

function filterFileList(event) {
    const query = (event ? event.target.value : '').toLowerCase().trim();
    if (!query) {
        renderFilesTable(window.allFileItems);
        return;
    }
    const filtered = window.allFileItems.filter(item => item.name.toLowerCase().includes(query));
    renderFilesTable(filtered);
}

function downloadFile(encodedPath) {
    const path = decodeURIComponent(encodedPath);
    window.location.href = `${API_BASE}/files/download?path=${encodeURIComponent(path)}`;
}

async function previewFile(encodedPath, filename, isImage) {
    const path = decodeURIComponent(encodedPath);
    const modal = document.getElementById('file-preview-modal');
    const titleEl = document.getElementById('preview-filename');
    const contentArea = document.getElementById('preview-content-area');
    const downloadBtn = document.getElementById('preview-download-btn');
    const iconEl = document.getElementById('preview-icon');

    if (!modal || !contentArea) return;

    titleEl.textContent = filename;
    iconEl.textContent = isImage ? '🖼️' : '📄';
    downloadBtn.onclick = () => downloadFile(encodedPath);
    contentArea.innerHTML = '<p class="text-slate-500 text-center py-8">Loading preview...</p>';
    modal.classList.remove('hidden');

    if (isImage) {
        contentArea.innerHTML = `
            <div class="flex items-center justify-center p-4">
                <img src="${API_BASE}/files/download?path=${encodeURIComponent(path)}" alt="${escapeHtml(filename)}" class="max-w-full max-h-[60vh] object-contain rounded-lg border border-slate-800 shadow-xl">
            </div>
        `;
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/files/preview?path=${encodeURIComponent(path)}`);
        if (response.ok) {
            const data = await response.json();
            contentArea.innerHTML = `<pre class="text-slate-300 font-mono text-xs whitespace-pre-wrap leading-relaxed overflow-x-auto">${escapeHtml(data.content)}</pre>`;
        } else {
            contentArea.innerHTML = '<p class="text-rose-400 text-center py-8">Unable to preview file. File may be binary or access denied.</p>';
        }
    } catch (e) {
        contentArea.innerHTML = `<p class="text-rose-400 text-center py-8">Error: ${e.message}</p>`;
    }
}

function hideFilePreview() {
    const modal = document.getElementById('file-preview-modal');
    if (modal) modal.classList.add('hidden');
}

// Folder creation
function showCreateFolderModal() {
    document.getElementById('new-folder-name').value = '';
    document.getElementById('create-folder-modal').classList.remove('hidden');
    document.getElementById('new-folder-name').focus();
}

function hideCreateFolderModal() {
    document.getElementById('create-folder-modal').classList.add('hidden');
}

async function handleCreateFolderSubmit(e) {
    e.preventDefault();
    const folderName = document.getElementById('new-folder-name').value.trim();
    if (!folderName) return;

    try {
        const response = await fetch(`${API_BASE}/files/mkdir`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ parentDir: window.currentBrowserPath, name: folderName })
        });
        if (response.ok) {
            hideCreateFolderModal();
            showToast('Folder Created', `Directory '${folderName}' created successfully.`, 'success');
            loadFiles(window.currentBrowserPath);
        } else {
            const err = await response.json();
            alert(`Failed to create folder: ${err.error || 'Unknown error'}`);
        }
    } catch (err) {
        alert(`Error: ${err.message}`);
    }
}

// Rename Item
function showRenameModal(encodedPath, currentName) {
    const path = decodeURIComponent(encodedPath);
    document.getElementById('rename-source-path').value = path;
    document.getElementById('rename-new-name').value = currentName;
    document.getElementById('rename-item-modal').classList.remove('hidden');
    document.getElementById('rename-new-name').focus();
}

function hideRenameModal() {
    document.getElementById('rename-item-modal').classList.add('hidden');
}

async function handleRenameSubmit(e) {
    e.preventDefault();
    const sourcePath = document.getElementById('rename-source-path').value;
    const newName = document.getElementById('rename-new-name').value.trim();
    if (!sourcePath || !newName) return;

    try {
        const response = await fetch(`${API_BASE}/files/rename`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sourcePath, newName })
        });
        if (response.ok) {
            hideRenameModal();
            showToast('Renamed', `Item renamed to '${newName}'.`, 'success');
            loadFiles(window.currentBrowserPath);
        } else {
            const err = await response.json();
            alert(`Failed to rename item: ${err.error || 'Unknown error'}`);
        }
    } catch (err) {
        alert(`Error: ${err.message}`);
    }
}

// Delete Item
async function deleteFileItem(encodedPath, itemName) {
    const path = decodeURIComponent(encodedPath);
    if (!confirm(`Are you sure you want to permanently delete "${itemName}"?`)) return;

    try {
        const response = await fetch(`${API_BASE}/files/delete`, {
            method: 'DELETE',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path })
        });
        if (response.ok) {
            showToast('Item Deleted', `"${itemName}" was removed.`, 'warning');
            loadFiles(window.currentBrowserPath);
        } else {
            const err = await response.json();
            alert(`Failed to delete item: ${err.error || 'Unknown error'}`);
        }
    } catch (err) {
        alert(`Error: ${err.message}`);
    }
}

// Drag & Drop / Upload
function handleDragOver(e) {
    e.preventDefault();
    e.stopPropagation();
    const zone = document.getElementById('file-dropzone');
    if (zone) zone.classList.add('border-cyan-500', 'bg-cyan-500/5');
}

function handleDragLeave(e) {
    e.preventDefault();
    e.stopPropagation();
    const zone = document.getElementById('file-dropzone');
    if (zone) zone.classList.remove('border-cyan-500', 'bg-cyan-500/5');
}

function handleFileDrop(e) {
    e.preventDefault();
    e.stopPropagation();
    const zone = document.getElementById('file-dropzone');
    if (zone) zone.classList.remove('border-cyan-500', 'bg-cyan-500/5');
    if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        uploadFileList(e.dataTransfer.files);
    }
}

function handleFileUpload(e) {
    if (e.target && e.target.files && e.target.files.length > 0) {
        uploadFileList(e.target.files);
        e.target.value = '';
    }
}

async function uploadFileList(files) {
    if (!window.currentBrowserPath) {
        alert('Please select a storage root before uploading files.');
        return;
    }

    const progressDiv = document.getElementById('file-upload-progress');
    const progressBar = document.getElementById('file-progress-bar');
    const progressLabel = document.getElementById('file-progress-label');
    const progressPercent = document.getElementById('file-progress-percent');

    if (progressDiv) progressDiv.classList.remove('hidden');

    let totalUploaded = 0;
    const totalFiles = files.length;

    for (let i = 0; i < totalFiles; i++) {
        const file = files[i];
        if (progressLabel) progressLabel.textContent = `Uploading ${file.name} (${i + 1}/${totalFiles})...`;

        const formData = new FormData();
        formData.append('destination', window.currentBrowserPath);
        formData.append('file', file);

        try {
            const response = await fetch(`${API_BASE}/files/upload`, {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                const err = await response.json();
                console.error(`Upload error for ${file.name}:`, err);
            } else {
                totalUploaded++;
            }
        } catch (e) {
            console.error(`Upload failed for ${file.name}:`, e);
        }

        const pct = Math.round(((i + 1) / totalFiles) * 100);
        if (progressBar) progressBar.style.width = `${pct}%`;
        if (progressPercent) progressPercent.textContent = `${pct}%`;
    }

    showToast('Upload Complete', `Uploaded ${totalUploaded} of ${totalFiles} file(s) successfully.`, 'success');

    setTimeout(() => {
        if (progressDiv) progressDiv.classList.add('hidden');
        if (progressBar) progressBar.style.width = '0%';
        if (progressPercent) progressPercent.textContent = '0%';
        loadFiles(window.currentBrowserPath);
    }, 1000);
}

// ==================== SYSTEM AUDIT & LOGS ====================
let currentLogView = 'audit';
let cachedAuditLogs = [];

function switchLogView(mode) {
    currentLogView = mode;
    const auditView = document.getElementById('audit-logs-view');
    const journalView = document.getElementById('system-journal-view');
    const modeSelect = document.getElementById('log-view-mode');

    if (modeSelect) modeSelect.value = mode;

    if (mode === 'audit') {
        if (auditView) auditView.classList.remove('hidden');
        if (journalView) journalView.classList.add('hidden');
        loadAuditLogs();
    } else {
        if (auditView) auditView.classList.add('hidden');
        if (journalView) journalView.classList.remove('hidden');
        loadSystemJournal();
    }
}

async function loadLogs() {
    if (currentLogView === 'audit') {
        await loadAuditLogs();
    } else {
        await loadSystemJournal();
    }
}

async function refreshLogs() {
    loadLogs();
}

async function loadAuditLogs() {
    const list = document.getElementById('audit-logs-list');
    if (!list) return;

    try {
        const response = await fetch(`${API_BASE}/logs/audit?limit=250`);
        if (!response.ok) return;
        cachedAuditLogs = await response.json();
        renderAuditLogsList(cachedAuditLogs);
    } catch (e) {
        list.innerHTML = `<p class="text-rose-400 text-sm text-center py-8">Failed to load audit logs: ${e.message}</p>`;
    }
}

function renderAuditLogsList(logs) {
    const list = document.getElementById('audit-logs-list');
    if (!list) return;

    if (!logs || logs.length === 0) {
        list.innerHTML = '<p class="text-slate-500 text-sm text-center py-8">No audit events recorded yet.</p>';
        return;
    }

    const levelColors = {
        Info: 'text-cyan-400 bg-cyan-500/10 border-cyan-500/20',
        Warning: 'text-amber-400 bg-amber-500/10 border-amber-500/20',
        Error: 'text-rose-400 bg-rose-500/10 border-rose-500/20',
        Security: 'text-purple-400 bg-purple-500/10 border-purple-500/20'
    };

    let html = '';
    logs.forEach(log => {
        const color = levelColors[log.level] || levelColors.Info;
        const timeStr = new Date(log.timestamp).toLocaleString();
        html += `
            <div class="bg-slate-900/60 border border-slate-800 hover:border-slate-700/80 p-3.5 rounded-xl flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 transition-all text-xs">
                <div class="flex items-center space-x-3">
                    <span class="px-2 py-0.5 rounded-md border font-mono font-bold text-[10px] uppercase ${color}">
                        ${log.level}
                    </span>
                    <span class="text-slate-400 font-mono text-[11px]">${timeStr}</span>
                    <span class="px-2 py-0.5 rounded bg-slate-800 text-slate-300 font-mono text-[10px]">
                        ${log.category}
                    </span>
                    <span class="font-bold text-white font-mono text-xs">
                        [${escapeHtml(log.user)}]
                    </span>
                </div>
                <div class="text-slate-300 flex-1 truncate sm:text-right font-mono" title="${escapeHtml(log.message)}">
                    ${escapeHtml(log.message)}
                </div>
            </div>
        `;
    });

    list.innerHTML = html;
}

function filterLogs() {
    if (currentLogView !== 'audit') return;

    const level = document.getElementById('log-level-filter')?.value || 'all';
    const category = document.getElementById('log-category-filter')?.value || 'all';
    const search = document.getElementById('log-search-input')?.value.toLowerCase().trim() || '';

    let filtered = cachedAuditLogs;

    if (level !== 'all') {
        filtered = filtered.filter(l => l.level && l.level.toLowerCase() === level.toLowerCase());
    }

    if (category !== 'all') {
        filtered = filtered.filter(l => l.category && l.category.toLowerCase() === category.toLowerCase());
    }

    if (search) {
        filtered = filtered.filter(l =>
            (l.message && l.message.toLowerCase().includes(search)) ||
            (l.user && l.user.toLowerCase().includes(search)) ||
            (l.details && l.details.toLowerCase().includes(search))
        );
    }

    renderAuditLogsList(filtered);
}

async function loadSystemJournal() {
    const pre = document.getElementById('system-journal-content');
    if (!pre) return;

    pre.textContent = 'Loading host OS journal output...';

    try {
        const response = await fetch(`${API_BASE}/logs/system?lines=100`);
        if (response.ok) {
            const data = await response.json();
            if (data.journal && data.journal.length > 0) {
                pre.textContent = data.journal.join('\n');
            } else {
                pre.textContent = 'No host journal lines returned.';
            }
        } else {
            pre.textContent = 'Failed to load host journal.';
        }
    } catch (e) {
        pre.textContent = `Error: ${e.message}`;
    }
}

// ==================== CONTAINER RUNTIME LOGS ====================
async function showContainerLogs(pluginId, pluginName) {
    const modal = document.getElementById('container-logs-modal');
    const title = document.getElementById('container-logs-title');
    const content = document.getElementById('container-logs-content');

    if (!modal || !content) return;

    title.textContent = `Runtime Logs: ${pluginName} (${pluginId})`;
    content.textContent = 'Fetching container logs from Docker daemon...';
    modal.classList.remove('hidden');

    try {
        const response = await fetch(`${API_BASE}/plugins/${encodeURIComponent(pluginId)}/logs`);
        if (response.ok) {
            const data = await response.json();
            content.textContent = data.logs || 'No log output captured from container.';
        } else {
            content.textContent = 'Failed to retrieve container logs.';
        }
    } catch (e) {
        content.textContent = `Error: ${e.message}`;
    }
}

function hideContainerLogs() {
    const modal = document.getElementById('container-logs-modal');
    if (modal) modal.classList.add('hidden');
}

// ==================== STRING ESCAPING UTILITY ====================
function escapeHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}



