#!/bin/bash
# Abre todo el sistema: puente del Arduino, juego de penales y Banco del técnico.
# Para grabar una sesión: botón «● Grabar» en el Banco del técnico.
# Doble clic en Finder. El puente queda corriendo en esta ventana (ciérrala para apagarlo).
cd "$(dirname "$0")/emg_penal"
pkill -f emg_puente.py 2>/dev/null; sleep 0.5
python3 -u emg_puente.py &
PUENTE=$!
sleep 2
open "$HOME/penales/Builds/Penales.app"
open "/Applications/Banco del técnico.app"
echo
echo "Listo. El Banco del técnico se conecta solo al juego."
echo "Para apagar el puente cierra esta ventana o presiona Ctrl+C."
wait $PUENTE
