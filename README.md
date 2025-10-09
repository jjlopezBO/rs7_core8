# RSpectrum7

Sistema automatizado de carga de datos eléctricos hacia Oracle.  
Servidor: `hfdspectrum (192.168.2.122)`  
Usuario de servicio: `ftpuser`

---

## 🧠 Descripción

RSpectrum7 es una aplicación en .NET que procesa archivos de lectura de sistemas fotovoltaicos y los carga en la base de datos Oracle CNDC.  
La ejecución está automatizada mediante **systemd** y programada para:

- **Ejecución diaria (ayer):** `00:45`
- **Ejecución continua (hoy):** cada 10 minutos

---

## ⚙️ Estructura de carpetas

| Ruta | Descripción |
|------|--------------|
| `/home/ftpuser/rspectrum7/` | Carpeta principal del sistema |
| `/home/ftpuser/rspectrum7/logs/` | Archivos de log (NLog) |
| `/home/ftpuser/rspectrum7/appsettings.json` | Configuración principal |
| `/run/rspectrum7/lock` | Archivo de bloqueo interno |

---

## 🧩 Servicios y timers

| Unidad | Descripción | Estado |
|---------|-------------|--------|
| `rspectrum7.service` | Procesa el día actual (parámetro `0`) | activo |
| `rspectrum7.timer` | Corre cada 10 min | activo |
| `rspectrum7-yesterday.service` | Procesa el día anterior (parámetro `1`) | activo |
| `rspectrum7-yesterday.timer` | Corre a las 00:45 | activo |

Verificar:
```bash
systemctl list-timers | grep rspectrum7
