// Puente seguro entre la pantalla y el proceso principal: solo expone la lectura de sesiones.
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('nube', {
  sesiones: () => ipcRenderer.invoke('nube:sesiones'),
  sesion: (id) => ipcRenderer.invoke('nube:sesion', id),
  alProgreso: (cb) => ipcRenderer.on('nube:progreso', (e, v) => cb(v))
});
