# Riivolution Interpreter Notes

## Como probar el interprete

```powershell
dotnet run --project .\src\RiivolutionIsoBuilder.RiivProbe\RiivolutionIsoBuilder.RiivProbe.csproj -- "..\Base\Nueva carpeta\riivolution\nmg.xml" PAL SB4P01
```

El probe imprime:

- patches reales definidos por `<patch root="...">`;
- `savegame`;
- carpetas `<folder external="..." disc="..." create="...">`;
- parches de memoria inline (`value`);
- parches que leen binarios (`valuefile`);
- una vista previa Ocarina solo para valores inline.

## Como probar desde la GUI

1. Selecciona o detecta el backup del juego.
2. Pulsa `Elegir XML`.
3. Selecciona un XML dentro de una carpeta `riivolution`, por ejemplo:

```text
Base\Nueva carpeta\riivolution\nmg.xml
```

4. Revisa o cambia el `ID6` sugerido. Para NMG PAL sobre `SB4P01`, la sugerencia es `NMGP01`.
5. Ejecuta `Crear mod`.

El ID6 reemplaza el uso real de `savegame`: como no podemos redirigir partidas en un backup como lo hace Riivolution, cambiamos el ID del juego para que use un save propio.

## Lo que hacia Patcher with Mouse

El flujo viejo tenia dos modos:

- `GCT`: `wstrt patch main.dol --add-sect <archivo.gct>`.
- `XML`: tomaba un XML preprocesado, reemplazaba el tag de region (`USA`, `PAL`, etc.) por `memory`, y llamaba:

```text
wit DOLPATCH main.dol "NEW=TEXT,0x80001800,1800" "XML=<patch>.xml.tmp" -o
```

Eso funcionaba porque los XML en `data/xml` ya estaban recortados y adaptados a `wit DOLPATCH`. No eran XML Riivolution completos.

## Lo que muestra el XML nativo de NMG

El XML real en `Base\Nueva carpeta\riivolution\nmg.xml` contiene:

- `options/section/option/choice` para decidir que patch activar;
- `patch root="/nmg"`;
- `savegame`;
- carpetas a copiar al disco (`AudioRes`, `CustomCode`, `LayoutData`, etc.);
- parches inline con `value`;
- parches con `valuefile`, por ejemplo:
  - `LayoutData/ErrorMessageArchive{$__region}.arc`;
  - `CustomCode/Loader{$__region}.bin`.

Con el probe actual, para `SB4P01` se resuelve:

- patch activo: `nmg`;
- `{$__region}` -> `P`;
- `valuefile="LayoutData/ErrorMessageArchiveP.arc"`;
- `valuefile="CustomCode/LoaderP.bin"`.

El comando conceptual para `wit DOLPATCH` debe ser:

```text
wit DOLPATCH <workdir>/sys/main.dol XML=<generated.xml> --source <mod-root>/nmg -o
```

`--source` es importante porque `wit DOLPATCH` busca alli los archivos referenciados por `valuefile`.

Nota: `DOLPATCH` puede devolver un codigo no-cero si alguna condicion `original` no coincide. En XML Riivolution esto puede ser normal cuando el mismo patch trae alternativas para regiones/versiones distintas. La app acepta ese caso si `wit` informa que guardo el DOL y no hubo errores duros como `Can't patch`.

Antes de generar el XML para `DOLPATCH`, la app filtra entradas con `original` contra el `main.dol` real. Esto emula mejor el comportamiento de Riivolution: las entradas destinadas a otra region/version simplemente no se aplican.

El builder de NSMBW hace una transformacion adicional (`80001800` -> `803482C0`) antes de parchear. Eso es especifico de NSMBW y su loader; para SMG/NMG el XML nativo declara `0x80001800`, asi que no se relocaliza por defecto.

## Direccion recomendada

La ruta mas general es convertir el XML Riivolution nativo a un plan interno:

1. Resolver opciones activas.
2. Resolver variables (`$__region`, `$__gameid`, `$__maker`).
3. Copiar carpetas/archivos segun `root`, `external` y `disc`.
4. Aplicar memoria:
   - `value`: se puede convertir a writes de 32-bit/Ocarina o a XML para `wit DOLPATCH`;
   - `valuefile`: se puede dejar como XML filtrado y pasar `--source` a `wit DOLPATCH`.
5. Mantener GCT solo como compatibilidad para mods ya traducidos.

La prueba actual ya cubre lectura de folders, savegame, `value` y `valuefile`.

En la GUI, por ahora las opciones se resuelven asi: si un XML tiene opciones y no hay selector detallado, se toma la primera choice de cada option. Esto permite convertir mods simples como NMG. El siguiente paso natural es mostrar esas choices antes de compilar.

