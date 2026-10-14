# Mambo Studio para Windows

Estudio nativo de grabación y edición en C# y WPF sobre .NET 10. Sin HTML, Electron, servidor web ni WebView. Interfaz en español.

## Ejecutar

Windows 10/11, SDK de .NET 10 y FFmpeg (`ffmpeg.exe` y `ffprobe.exe`) en PATH o en `tools` junto al ejecutable.

```powershell
.\launch.ps1
```

También: `dotnet build ScreenStudio.csproj -c Release` y abrir `bin\Release\net10.0-windows\MamboStudio.exe`. Se puede pasar la ruta de un archivo multimedia o de un proyecto como argumento.

## Espacio de trabajo

- El nombre del proyecto y el indicador de cambios pendientes aparecen en el título de la ventana.
- **Menu bar:** Archivo, Editar, Pistas, Ver y Configuración. Incluye importar, abrir, guardar y exportar.
- **Tool bar:** selección/movimiento, recorte, división, deshacer/rehacer, cortar/copiar/pegar, eliminar, marcar inicio/final, restablecer, texto, pistas, ajuste magnético y zoom. Los botones son cuadrados y muestran solo el icono; el nombre y el atajo aparecen en el tooltip. Las herramientas que no caben pasan al menú de más opciones.
- **Ventana:** título del proyecto centrado y botones de minimizar, maximizar/restaurar y cerrar dibujados y gestionados por Windows. Los iconos de la aplicación son vectores de Lucide 0.468.0, incluidos localmente y dibujados por WPF; su licencia está en `Assets/Icons/Lucide/LICENSE`.
- **Media bar:** principio/final, saltos de cinco segundos, avance por fotograma, reproducir/pausar, detener y grabar.
- **Paneles:** Archivos, Inspector, Lienzo, Movimiento, Apariencia, Transiciones, Pantallas y Captura. Las pestañas se pueden mover al otro lado con la flecha del panel. Se pueden contraer, ocultar o mostrar desde Ver; ambos lados pueden estar activos. Ver permite volver a añadir cualquier panel.
- **Archivos:** miniaturas reales, vistas de tarjetas y lista, búsqueda, filtros por tipo y ordenación por nombre, duración o tipo. Importar desde este panel añade a la biblioteca; doble clic, el botón de insertar o arrastrar a la línea de tiempo añade el clip. Eliminar una referencia sin uso de la biblioteca conserva el archivo original.
- **Tema:** claro u oscuro desde Ver. El tema, la distribución de paneles y los valores predeterminados de salida se conservan entre sesiones.

## Edición multipista

Importa vídeos, imágenes o audio desde Archivo, el panel Archivos o arrastrando archivos a la ventana. Al importar un vídeo con sonido se crea un clip visual y otro de audio en pistas separadas. Son independientes: mover o recortar uno no modifica el otro.

Las pistas visuales admiten vídeo, imágenes y texto. Las de audio sólo admiten audio. Las pistas superiores se componen por encima de las inferiores. Puedes añadir pistas y eliminar las que estén vacías; el Inspector permite renombrarlas y desactivarlas.

Con Seleccionar (`V`), arrastra un clip en el tiempo o a otra pista compatible. El ajuste magnético alinea con clips y cabezal; mantén Mayús para omitirlo. No se permiten solapamientos dentro de una misma pista: usa otra pista para superponer elementos. Pegar coloca el clip en el siguiente espacio libre a partir del cabezal.

Con Recortar (`T`), arrastra la mitad correspondiente al extremo que quieras ajustar. Dividir (`C`) corta al hacer clic; `S` divide el clip seleccionado en el cabezal. `I` y `O` marcan los límites desde el cabezal. Restablecer recupera la duración original si hay espacio. Ctrl+Z/Ctrl+Y deshacen y rehacen las ediciones.

El Inspector permite editar posición, duración, desplazamiento en el archivo, escala, posición X/Y, opacidad, volumen, velocidad (25–400 %), fundidos de audio y contenido de texto. Los cambios se guardan solos al salir del campo (Enter o clic fuera). La sección Animación añade keyframes de opacidad, escala y posición (clips visuales) o volumen (audio) en el cabezal, con interpolación suave en previa y exportación. El audio aplica un micro-fundido anti-clic en cada corte y el botón Fundido cruzado iguala fundidos entre clips adyacentes. Lienzo configura dimensiones, margen y fondo, también automáticamente, con presets Horizontal 16:9, Vertical 9:16, Cuadrado 1:1 y Retrato 4:5. La altura de la línea de tiempo se ajusta arrastrando el separador que hay encima de la toolbar.

En la vista previa, haz clic en un vídeo, imagen o texto para seleccionarlo. Arrastra las esquinas o los puntos de los lados para cambiar su tamaño conservando las proporciones; arrastra dentro del marco para moverlo. Escape cancela el gesto y Ctrl+Z deshace el cambio completo. El tamaño y la posición se guardan en el proyecto y se aplican a la exportación.

## Grabación y salida

En **Configuración → Fuentes y región de captura**, elige una pantalla conectada, dos pantallas simultáneas, región del escritorio, ventana o región de una ventana. Las regiones se pueden seleccionar con el mouse o por coordenadas en píxeles. Los ajustes se guardan solos al cambiar cualquier opción y la lista de dispositivos (micrófono, salida de audio del sistema y cámara) se detecta automáticamente; se graban e importan como pistas independientes. El panel muestra el origen en vivo al estilo OBS (se actualiza solo al cambiar de pantalla, ventana o región, incluyendo ventanas con aceleración GPU) y medidores de nivel en vivo de micrófono y sistema para comprobar que hay señal antes de grabar. Arrastrar sobre la vista previa fija la región de captura: en los modos de pantalla o ventana cambia solo al modo de región correspondiente. La cámara se coloca inicialmente en una esquina y se puede reposicionar en el Inspector.

La grabación tiene cuenta atrás cancelable y usa los FPS del proyecto. La app se minimiza al comenzar. Detén desde el botón de la media bar o con **Ctrl+Shift+F9**. Las capturas fuente se guardan en `Vídeos\ScreenStudio\Recordings`: pantalla y cámara en MP4 H.264, micrófono y audio del sistema en WAV. Los desfases de inicio de los dispositivos se aplican al insertar sus pistas.

## Dos pantallas

En Captura elige **Dos pantallas**, selecciona cada monitor y aplica las fuentes. Se graban simultáneamente en archivos MP4 independientes, cada uno con su propio archivo de cursor, y se colocan en pistas distintas con los desfases de inicio conservados. El micrófono, audio del sistema y cámara se graban una sola vez para la sesión.

La distribución inicial puede ser izquierda/derecha, arriba/abajo o pistas separadas, con orden invertido. Selecciona uno de los clips y abre **Ver → Pantallas** para cambiar la distribución después de grabar. Sus botones de pantalla cambian la fuente visible desde el cabezal: dividen las pistas en ese punto y mantienen los archivos originales. Los cambios se pueden recortar, mover y deshacer como los demás clips.

## Transiciones

Selecciona un clip visual y abre **Ver → Transiciones** o el acceso del Inspector. Elige corte, fundido cruzado de entrada, fundido a negro o barrido desde izquierda/derecha, con duración y estado activo independientes para entrada y salida. Los cambios se aplican inmediatamente y se ven en un carril de Transiciones en el timeline.

Entre clips consecutivos de una misma pista, el fundido cruzado y los barridos abarcan ambos lados del corte. Mantienen el primer y último fotograma durante la superposición, conservando la duración y los tiempos del montaje. Si no hay un clip adyacente, la entrada se aplica sobre el fondo. La salida permite fundido o barrido del clip. Los originales permanecen limpios y el proyecto conserva estos ajustes.

## Cursor, zoom y apariencia

Todas las nuevas grabaciones guardan el vídeo limpio y, por separado, posiciones, visibilidad y clics en un archivo `.mp4.cursor.json`. El cursor, seguimiento y zoom se componen en el editor y en la exportación: el MP4 fuente conserva su encuadre original sin esos efectos. El proyecto también conserva los datos de cursor. Al importar un vídeo que tenga su archivo de cursor al lado, se recuperan automáticamente. Esta separación también se aplica si las preferencias antiguas tenían desactivado el cursor editable.

Selecciona el clip y abre **Ver → Movimiento**, o usa el acceso desde el Inspector. Puedes desactivar el zoom, acercar en los clics, seguir siempre el cursor o fijar un foco manual. Se ajustan aumento (1–4×), respuesta del seguimiento, duración de transición y permanencia tras clics, tamaño y suavizado del cursor, resaltado de clics y ocultación por inactividad. Los vídeos sin datos del cursor admiten zoom manual; no se reconstruye la posición del mouse desde los píxeles.

En **Movimiento → Imagen…** puedes sustituir la flecha por una imagen PNG, ICO, JPEG o BMP y definir su punto activo X/Y (la punta o punto de clic dentro de la imagen). Se conserva la transparencia de PNG y la imagen se incorpora al proyecto; no depende de su ruta original. Las casillas y el modo de zoom se aplican inmediatamente, también durante la reproducción. Los campos numéricos se confirman con Enter o al salir del campo. Estos cambios admiten deshacer/rehacer. Mostrar cursor, resaltar clics y modo de zoom son opciones independientes y no modifican el archivo de vídeo original. Las grabaciones antiguas que ya tengan el cursor incrustado no permiten eliminarlo de los píxeles.

Debajo de cada pista con vídeo aparecen los carriles **Cursor**, **Clics** y **Zoom**. Los clics son marcadores y los zooms son intervalos; al seleccionar uno se abre su edición en Movimiento. Arrastra un marcador o el centro de un intervalo para moverlo, y los extremos de un zoom para cambiar su duración. El menú del botón derecho permite activar/desactivar o eliminar un evento; Supr elimina el evento seleccionado y conserva el vídeo. El doble clic sobre un espacio libre del carril Zoom añade un intervalo dentro del vídeo. El doble clic en la banda Cursor lo activa/desactiva.

Cada zoom admite su propio aumento y estado activo. **Sin zoom** desactiva todos los intervalos del clip sin borrarlos; **Resaltar clics** hace lo mismo con sus marcadores. Las ediciones de resaltados de clic y zoom son independientes. Los eventos se guardan con el proyecto y conservan su alineación al mover, dividir o recortar el vídeo.

**Apariencia** configura esquinas redondeadas, borde, sombra y animaciones de entrada/salida por opacidad. **Lienzo** configura fondo sólido o degradado, margen y dimensiones. Estos ajustes se ven en la vista previa y se aplican a la exportación.

## Exportación y proyectos

Configuración permite elegir FPS (24, 25, 30, 50 o 60), codificador de vídeo (H.264, H.265 o VP9), audio (AAC, Opus o PCM) y contenedor (MP4, MKV, WebM o MOV). Se aplican al proyecto y como predeterminados de proyectos nuevos. Los codificadores y contenedores seleccionados se usan en la exportación; la captura fuente usa H.264. WebM requiere VP9/Opus, MP4 y MOV H.264/H.265 con AAC. La instalación de FFmpeg debe incluir los codificadores seleccionados.

La exportación compone los clips visuales y mezcla el audio respetando posiciones, recortes, opacidad, volumen, velocidad, fundidos y keyframes. Incluye los espacios vacíos del montaje. Con `Alt+I` / `Alt+O` se fija el rango a exportar (se muestra en la regla; `Alt+X` lo limpia). Puede cancelarse; el destino se reemplaza sólo al completar correctamente. Los originales no se modifican. Desde Configuración se activan los subtítulos `.srt` (generados desde los clips de texto) y la estabilización de vídeo (dos pasadas, más lenta). La pestaña Cola (añadible desde Ver) encola copias del proyecto y las exporta en orden, con estado por trabajo, cancelación y continuación ante fallos puntuales.

Los proyectos `.studio` conservan pistas, biblioteca, clips y ajustes, con referencias a los archivos originales. También se importan los anteriores `.screenstudio` de una sola pista. Si falta un original se solicita localizarlo. Cada dos minutos se guarda una copia de recuperación (`autoguardado`): al abrir la app se ofrece restaurarla y se elimina al guardar o cerrar.

## Límites actuales

La captura usa `gdigrab` de FFmpeg, audio nativo WASAPI mediante NAudio y cámara mediante DirectShow. Las ventanas minimizadas, contenido protegido y algunas superficies aceleradas por GPU pueden no capturarse correctamente. Cámara y micrófono requieren dispositivos y permisos de Windows disponibles. Las comprobaciones automatizadas no validan la grabación con hardware de audio o cámara real.

El cursor editable usa una flecha o imagen elegida y muestreo aproximado de 16 ms; todavía no conserva automáticamente cada cambio de forma del cursor del sistema. Las animaciones disponibles son zoom/seguimiento y fundidos por opacidad: no hay editor de fotogramas clave ni agrupación automática del vídeo con su audio. La biblioteca de audio muestra metadatos e icono, todavía sin forma de onda.

La reproducción utiliza MediaPlayer y los códecs de Windows. Algunos formatos exportables por FFmpeg pueden no reproducirse en el editor; se muestra un aviso si Windows los rechaza. Los efectos se renderizan nativamente con WPF por CPU y utilizan intermediarios sin pérdida durante la exportación; los proyectos largos o de alta resolución necesitan tiempo y espacio temporal adicional.

## Validación

```powershell
dotnet build ScreenStudio.csproj -c Release
dotnet run --project tests\ScreenStudio.Checks.csproj -c Release
# Incluye una captura real de una región de una ventana de prueba creada por la suite:
dotnet run --project tests\ScreenStudio.Checks.csproj -c Release -- --capture-fixture
```

Las comprobaciones generan medios sintéticos locales y validan edición multipista, exportación, mezcla de audio, persistencia, protección de originales, cancelación, datos de cursor, seguimiento y zoom, transparencia de bordes, fondo y apariencia. Ejercitan los controles WPF, los paneles y la enumeración de dispositivos sin activar micrófono o cámara. Generan capturas de la interfaz en `artifacts/checks`. Con `--capture-fixture`, graban únicamente una región de su propia ventana de prueba y comprueban que se decodifica y conserva los datos del cursor.

