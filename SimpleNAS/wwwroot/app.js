// SimpleNAS Frontend
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
            break;
        case 'shares':
            loadShares();
            break;
        case 'cloud':
            loadCloud();
            break;
        case 'network':
            loadNetwork();
            break;
    }
}

// Dashboard
async function loadDashboard() {
    try {
        const response = await fetch(`${API_BASE}/system/status`);
        const data = await response.json();
        
        document.getElementById('cpu-usage').textContent = `${data.cpu.percent.toFixed(1)}%`;
        document.getElementById('mem-usage').textContent = `${data.memory.percent.toFixed(1)}%`;
        document.getElementById('disk-usage').textContent = `${data.disk.percent.toFixed(1)}%`;
        
        // Load services
        const servicesResp = await fetch(`${API_BASE}/system/services`);
        const services = await servicesResp.json();
        
        const servicesList = document.getElementById('services-list');
        servicesList.innerHTML = '';
        
        for (const [name, status] of Object.entries(services.services)) {
            const statusColor = status === 'active' ? 'green' : 'red';
            servicesList.innerHTML += `
                <div class="flex justify-between items-center p-2 bg-gray-50 rounded">
                    <span class="font-semibold">${name}</span>
                    <span class="text-${statusColor}-600">${status}</span>
                </div>
            `;
        }
    } catch (error) {
        console.error('Dashboard load error:', error);
    }
}

// ZFS
async function loadZFSPools() {
    try {
        const response = await fetch(`${API_BASE}/zfs/pools`);
        const data = await response.json();
        
        const poolsList = document.getElementById('pools-list');
        poolsList.innerHTML = '';
        
        if (data.pools.length === 0) {
            poolsList.innerHTML = '<p class="text-gray-500">No ZFS pools found</p>';
            return;
        }
        
        data.pools.forEach(pool => {
            const healthColor = pool.health === 'ONLINE' ? 'green' : 'red';
            poolsList.innerHTML += `
                <div class="card">
                    <div class="flex justify-between items-center">
                        <div>
                            <h3 class="text-xl font-bold">${pool.name}</h3>
                            <p class="text-gray-600">Size: ${pool.size} | Used: ${pool.allocated} | Free: ${pool.free}</p>
                        </div>
                        <span class="text-${healthColor}-600 font-bold">${pool.health}</span>
                    </div>
                </div>
            `;
        });
    } catch (error) {
        console.error('ZFS load error:', error);
    }
}

async function showCreatePool() {
    // Load available devices
    const response = await fetch(`${API_BASE}/zfs/devices`);
    const data = await response.json();
    
    const devicesList = document.getElementById('devices-list');
    devicesList.innerHTML = '';
    
    data.devices.forEach(device => {
        devicesList.innerHTML += `
            <label class="flex items-center">
                <input type="checkbox" name="devices" value="${device.name}" class="mr-2">
                <span>${device.name} (${device.size})</span>
            </label>
        `;
    });
    
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
        alert('Please select at least one device');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/zfs/pools`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({name, vdev_type, devices})
        });
        
        if (response.ok) {
            alert('Pool created successfully!');
            hideCreatePool();
            loadZFSPools();
        } else {
            const error = await response.json();
            alert(`Failed to create pool: ${error.detail}`);
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
    }
}

// Shares
async function loadShares() {
    try {
        // Load SMB shares
        const smbResp = await fetch(`${API_BASE}/shares/smb`);
        const smbData = await smbResp.json();
        
        const smbList = document.getElementById('smb-list');
        smbList.innerHTML = '';
        
        if (smbData.shares.length === 0) {
            smbList.innerHTML = '<p class="text-gray-500">No SMB shares</p>';
        } else {
            smbData.shares.forEach(share => {
                smbList.innerHTML += `
                    <div class="p-3 bg-white rounded shadow">
                        <div class="font-bold">${share.name}</div>
                        <div class="text-sm text-gray-600">${share.config.path || 'N/A'}</div>
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
            nfsList.innerHTML = '<p class="text-gray-500">No NFS exports</p>';
        } else {
            nfsData.exports.forEach(exp => {
                nfsList.innerHTML += `
                    <div class="p-3 bg-white rounded shadow">
                        <div class="font-bold">${exp.path}</div>
                        <div class="text-sm text-gray-600">${exp.clients.join(', ')}</div>
                    </div>
                `;
            });
        }
    } catch (error) {
        console.error('Shares load error:', error);
    }
}

// Network
async function loadNetwork() {
    try {
        // Tailscale status
        const tailscaleResp = await fetch(`${API_BASE}/network/tailscale/status`);
        const tailscaleData = await tailscaleResp.json();
        
        const statusDiv = document.getElementById('tailscale-status');
        if (tailscaleData.running) {
            statusDiv.innerHTML = '<p class="text-green-600 font-bold">Running</p>';
        } else if (tailscaleData.installed) {
            statusDiv.innerHTML = '<p class="text-yellow-600">Installed but not running</p>';
        } else {
            statusDiv.innerHTML = '<p class="text-red-600">Not installed</p>';
        }
        
        // Network interfaces
        const ifacesResp = await fetch(`${API_BASE}/network/interfaces`);
        const ifacesData = await ifacesResp.json();
        
        const ifacesList = document.getElementById('interfaces-list');
        ifacesList.innerHTML = '';
        
        ifacesData.interfaces.forEach(iface => {
            ifacesList.innerHTML += `
                <div class="p-3 bg-gray-50 rounded mb-2">
                    <div class="font-bold">${iface.name}</div>
                    <div class="text-sm text-gray-600">State: ${iface.state}</div>
                    <div class="text-sm">
                        ${iface.addresses.map(a => a.address).join(', ')}
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
            alert('Tailscale started!');
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
            alert('Tailscale stopped');
            loadNetwork();
        }
    } catch (error) {
        alert(`Error: ${error.message}`);
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

// Cloud Storage Manager
async function loadCloud() {
    try {
        const response = await fetch(`${API_BASE}/cloud/status`);
        const data = await response.json();
        
        const rcloneDiv = document.getElementById('rclone-status');
        if (data.rcloneMounted) {
            rcloneDiv.innerHTML = `<p class="text-green-600 font-bold">Mounted (Active)</p>
                                   <p class="text-sm text-gray-500">Mountpoint: ${data.rclonePath}</p>`;
        } else {
            rcloneDiv.innerHTML = '<p class="text-red-600 font-semibold">Not Mounted</p>';
        }
        
        const mergerfsDiv = document.getElementById('mergerfs-status');
        if (data.mergerfsMounted) {
            mergerfsDiv.innerHTML = `<p class="text-green-600 font-bold">Union Mounted (Active)</p>
                                     <p class="text-sm text-gray-500">Mountpoint: ${data.mergerfsPath}</p>`;
        } else {
            mergerfsDiv.innerHTML = '<p class="text-red-600 font-semibold">Not Mounted</p>';
        }

        const syncDiv = document.getElementById('sync-status');
        if (data.syncActive) {
            syncDiv.innerHTML = `<span class="text-purple-600 font-bold animate-pulse">Running Background Sync...</span>`;
        } else {
            syncDiv.innerHTML = `<span class="text-gray-500">Idle (Last run completed successfully)</span>`;
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
