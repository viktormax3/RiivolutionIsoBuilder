# Riivolution Native Samples

Coloca aqui ejemplos reales de mods Riivolution sin preprocesar.

Estructura sugerida por muestra:

```text
samples/riivolution-native/<mod-id>/
  riivolution/
    <mod>.xml
  files/
    ...
```

La idea es usar esta carpeta como banco de pruebas para el interprete Riivolution:

- leer XML nativo;
- resolver opciones/secciones por region;
- mapear reemplazos de archivos;
- convertir parches `<memory offset="..." value="...">` a una representacion aplicable;
- detectar casos que todavia requieren reglas manuales.


