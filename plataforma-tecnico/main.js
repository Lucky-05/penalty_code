// Ventana de la aplicación de escritorio. La pantalla es la misma de index.html.
const { app, BrowserWindow, ipcMain } = require('electron');
const os = require('os');
const path = require('path');
const fs = require('fs');

function createWindow() {
  const win = new BrowserWindow({
    width: 1560, height: 980, minWidth: 900, minHeight: 600,
    title: 'Banco del técnico', backgroundColor: '#f5f6f8',
    titleBarStyle: 'hiddenInset', trafficLightPosition: { x: 18, y: 18 },
    webPreferences: { contextIsolation: true, nodeIntegration: false, preload: path.join(__dirname, 'preload.js') }
  });
  const ses = win.webContents.session;
  // Arduino por USB: se elige solo el primer puerto que parezca una placa.
  ses.on('select-serial-port', (event, ports, wc, done) => {
    event.preventDefault();
    const port = ports.find(p => /usbmodem|usbserial|wchusb|SLAB/i.test(p.portName || '')) || ports[0];
    done(port ? port.portId : '');
  });
  ses.setPermissionCheckHandler((wc, permission) => permission === 'serial');
  ses.setDevicePermissionHandler(details => details.deviceType === 'serial');
  win.loadFile(path.join(__dirname, 'index.html'));

  // Para pruebas: BANCO_CAPTURA=ruta.png guarda una imagen de la ventana y cierra.
  if (process.env.BANCO_CAPTURA) {
    win.webContents.once('did-finish-load', () => setTimeout(async () => {
      if (process.env.BANCO_ACCION) { console.log("ACCION:", await win.webContents.executeJavaScript(process.env.BANCO_ACCION).catch(e => "error " + e)); await new Promise(r => setTimeout(r, Number(process.env.BANCO_ESPERA || 0))); }
      const view = process.env.BANCO_VISTA;
      if (view) { await win.webContents.executeJavaScript(`document.querySelector('#nav button[data-t=${view}]').click()`); await new Promise(r => setTimeout(r, 1500)); }
      fs.writeFileSync(process.env.BANCO_CAPTURA, (await win.webContents.capturePage()).toPNG());
      app.quit();
    }, 6000));
  }
}

// ── Sesiones grabadas en Supabase ─────────────────────────────────────────────────────
// La llave vive en ~/.config/emg-penales/supabase.env (la misma que usa el puente del juego);
// nunca llega a la pantalla.
function nubeCfg() {
  try {
    const c = {};
    fs.readFileSync(path.join(os.homedir(), '.config/emg-penales/supabase.env'), 'utf8').split('\n').forEach(l => {
      const i = l.indexOf('=');
      if (i > 0) c[l.slice(0, i).trim()] = l.slice(i + 1).trim();
    });
    return c.SUPABASE_URL && c.SUPABASE_KEY ? c : null;
  } catch (e) { return null; }
}

async function rest(q) {
  const c = nubeCfg();
  if (!c) throw new Error('falta el archivo ~/.config/emg-penales/supabase.env');
  const r = await fetch(c.SUPABASE_URL + '/rest/v1/' + q, { headers: { apikey: c.SUPABASE_KEY, Authorization: 'Bearer ' + c.SUPABASE_KEY } });
  if (!r.ok) throw new Error('Supabase ' + r.status + ': ' + (await r.text()).slice(0, 200));
  return r.json();
}

async function todo(q, paso, alAvanzar) {          // PostgREST entrega a lo más 1000 filas por consulta
  const out = [];
  for (let o = 0; ; o += paso) {
    const parte = await rest(q + '&limit=' + paso + '&offset=' + o);
    out.push(...parte);
    if (alAvanzar) alAvanzar(out.length);
    if (parte.length < paso) return out;
  }
}

ipcMain.handle('nube:sesiones', () => rest('sesiones?select=*&order=inicio.desc&limit=1000'));
ipcMain.handle('nube:sesion', async (e, id) => {
  if (!/^[0-9a-f-]{36}$/.test(id)) throw new Error('sesión inválida');
  const ses = await rest('sesiones?select=*&id=eq.' + id);
  if (!ses.length) throw new Error('la sesión ya no existe');
  const eventos = await todo('eventos?select=muestra,tipo,valor&sesion_id=eq.' + id + '&order=muestra', 1000);
  const bloques = await todo('bloques?select=n0,a2,a3&sesion_id=eq.' + id + '&order=n0', 120,
    n => e.sender.send('nube:progreso', n));
  return { sesion: ses[0], eventos, bloques };
});

app.whenReady().then(createWindow);
app.on('window-all-closed', () => app.quit());
