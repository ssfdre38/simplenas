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
            loadZfsDevices();
            break;
        case 'shares':
            loadShares();
            break;
        case 'network':
            loadNetwork();
            loadFirewallStatus();
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
        const response = await fetch(`${API_BASE}/zfs/pools`);
        const data = await response.json();
        
        const poolsList = document.getElementById('pools-list');
        poolsList.innerHTML = '';
        
        if (data.pools.length === 0) {
            poolsList.innerHTML = '<p class="text-slate-500 text-sm">No ZFS pools found. Create a pool to get started.</p>';
            return;
        }
        
        data.pools.forEach(pool => {
            const isOnline = pool.health === 'ONLINE';
            const healthClass = isOnline ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20' : 'text-rose-400 bg-rose-500/10 border-rose-500/20';
            
            poolsList.innerHTML += `
                <div class="bg-slate-900/40 border border-slate-800 p-5 rounded-xl space-y-3">
                    <div class="flex justify-between items-center">
                        <div>
                            <h4 class="text-lg font-bold text-white">${pool.name}</h4>
                            <p class="text-xs text-slate-400">Total Capacity: ${pool.size}</p>
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

// Shares Listing
async function loadShares() {
    try {
        // Load SMB shares
        const smbResp = await fetch(`${API_BASE}/shares/smb`);
        const smbData = await smbResp.json();
        
        const smbList = document.getElementById('smb-list');
        smbList.innerHTML = '';
        
        if (smbData.shares.length === 0) {
            smbList.innerHTML = '<p class="text-slate-500 text-xs">No active Samba shares configured</p>';
        } else {
            smbData.shares.forEach(share => {
                smbList.innerHTML += `
                    <div class="p-4 bg-slate-900/40 border border-slate-800 rounded-xl flex justify-between items-center">
                        <div>
                            <div class="font-bold text-white text-base">${share.name}</div>
                            <div class="text-xs text-slate-400 font-mono mt-1">${share.config.path || 'N/A'}</div>
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

// Initialize - show dashboard
document.addEventListener('DOMContentLoaded', () => {
    showTab('dashboard');
    
    // Auto-refresh dashboard every 5 seconds
    setInterval(() => {
        if (!document.getElementById('tab-dashboard').classList.contains('hidden')) {
            loadDashboard();
        }
    }, 5000);
});
