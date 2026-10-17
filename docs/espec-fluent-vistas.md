# Especificación Fluent — vistas de Briple.Trainer

Spec de estructura por vista, derivada de la auditoría de código y de la
referencia visual del PWA. El lenguaje visual es Fluent (WinUI 3 / Windows 11);
el PWA aporta la disposición de la información del plan, no su estilo.
Cada sección: contenido real (del código), patrón Fluent, estructura, métricas,
estados y cambios concretos contra la implementación actual.

Archivos afectados por vista:

| Vista | Archivos |
|---|---|
| Shell | `Program.fs` |
| Login | `Views/Login.fs` |
| Atletas | `Components/AthleteList.fs` |
| Detalle atleta | `Views/Athletes.fs` |
| Nuevo atleta | `Components/NewAthleteForm.fs` |
| Generar (wizard) | `Views/Generate.fs`, `Components/WizardBrief.fs`, `Components/MemoryReview.fs` |
| Revisión | `Views/Review.fs`, `Components/PlanReview.fs`, `Components/NoteSection.fs`, `Components/MemoryReview.fs` |
| Ajustes | `Views/Settings.fs` |
| Diálogos | `Dialogs.fs` |
| Toasts | `Toasts.fs` |

---

## 0. Fundamentos compartidos

Estas reglas aplican a todas las vistas. Una sola vez en el repo, en la capa
de estilos app-level; ninguna vista repite estos valores.

### 0.1 Tokens por tema (ThemeDictionaries)

Los pinceles actuales de `Theme.fs` son hex fijos de tema claro sobre ventana
oscura. Reemplazo: un `ResourceDictionary` con `ThemeDictionaries` Light/Dark,
leído con `{DynamicResource}`. Claves y valores:

| Clave | Dark | Light |
|---|---|---|
| `AppTextPrimary` | `#FFFFFF` | `#1A1A1A` |
| `AppTextSecondary` | `#C9FFFFFF` | `#9A000000` |
| `AppTextTertiary` | `#8AFFFFFF` | `#71000000` |
| `AppAccent` | `#2D7DD2` (marca) | `#2D7DD2` |
| `AppAccentText` | `#FFFFFF` | `#FFFFFF` |
| `AppAccentTonalFill` | `#332D7DD2` | `#1A2D7DD2` |
| `AppCardFill` | `#0DFFFFFF` | `#B3FFFFFF` |
| `AppCardStroke` | `#15FFFFFF` | `#1F000000` |
| `AppCardFillHover` | `#0AFFFFFF` | `#0D000000` |
| `AppError` | `#FF99664D` (legible en oscuro) | `#C0392B` |
| `AppErrorFill` | `#1AFF9966` | `#1AC0392B` |

Regla dura: cero `Brushes.` ni hex inline en vistas. Una vista que necesita
color nombra un recurso.

### 0.2 Rampa tipográfica

Un solo rampa; los tamaños sueltos por archivo desaparecen.

| Rol | Tamaño | Peso | Uso |
|---|---|---|---|
| `Title` | 28 | SemiBold | Título de página (1 por vista) |
| `Subtitle` | 20 | SemiBold | Sección dentro de página |
| `BodyStrong` | 14 | SemiBold | Nombre de fila, etiqueta destacada |
| `Body` | 14 | Regular | Texto por defecto |
| `Caption` | 12 | Regular | Metadatos, secundario, validación |

Clases de estilo: `title`, `subtitle`, `body`, `body-strong`, `caption`.

### 0.3 Espaciado y contenedores

- Base 4 px. Escala usada: 4, 8, 12, 16, 24.
- Margen de página: 24 en los cuatro lados.
- Separación: 4 dentro de un grupo, 8 entre campo y campo, 16 entre secciones,
  24 antes de una nueva Subtitle.
- Ancho máximo de contenido: formularios 560, lectura 760 (ya existe), plan en
  columnas: sin máximo (scroll horizontal).
- Tarjeta (contenedor de contenido en Fluent): relleno `AppCardFill`,
  borde 1 px `AppCardStroke`, radio 4, padding 16.
- Alturas: botón y TextBox 32; fila de lista de una línea 40, de dos líneas 64.
- Radio: 4 en todo; nada de `CornerRadius(0)` ni píldoras arbitrarias.

### 0.4 Estados de control

- Hover: relleno `AppCardFillHover` (alfa), nunca borde.
- Presionado: alfa mayor (≈8%).
- Deshabilitado: texto `AppTextTertiary`, sin relleno de acento.
- Selección de fila: píldora de acento de 3×16-24 px en el borde inicial de la
  fila + texto primario; sin fondo de acento.

### 0.5 Botones

| Variante | Aspecto | Uso (máximo uno por vista) |
|---|---|---|
| Primario | fondo `AppAccent`, texto `AppAccentText`, radio 4, alto 32 | la acción principal de la página |
| Estándar | fondo transparente, borde 1 px `AppCardStroke`, alto 32 | acciones secundarias |
| Texto (subtle) | sin fondo ni borde, hover alfa; texto `Body` | acciones terciarias y comandos de overflow |
| Hipervínculo | texto `AppAccent` | enlaces dentro de prosa; no como botón de acción |

Iconos: 16 px, heredan el color del texto. El icono sin etiqueta solo en
overflow "…".

### 0.6 Plantilla de página (anatomía)

```
┌──────────────────────────────────────────────────────────┐
│ Título (Title 28)                    [Secundario] [Primario] │  ← fila de encabezado, 24 px margen
│                                                          │
│  cuerpo (scroll), máx. 560-760 centrado                  │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

- Encabezado: título a la izquierda, comandos a la derecha, en la misma línea,
  alineados al centro vertical. Primario siempre el más a la derecha.
- Sin CommandBar de ancho completo bajo el título. Overflow "…" solo con 3+
  comandos secundarios.
- Footer solo en wizards y diálogos de flujo. Prohibido en páginas de lectura.

### 0.7 Navegación

El shell es dueño del botón atrás y del título de ventana. Las páginas no
dibujan su propio "Atrás".

---

## 1. Shell (`Program.fs`)

**Contenido hoy:** SplitView CompactInline (riel 48, panel 220), botón
hamburguesa manual, filas de navegación manuales (icono 48 + etiqueta ligada al
stream abierto), botón "Atrás" en un `DockPanel` propio sobre el contenido,
título de ventana por sección.

**Patrón:** NavigationView (spec WinUI). Avalonia 12 no lo trae (verificado);
se implementa sobre el SplitView existente pero con el spec completo, o con un
control propio del repo. Decisión abierta (ver §12).

**Estructura:**

```
┌──────┬─────────────────────────────────────────┐
│ riel │  ← atrás   Título de página (del router) │  ← fila de chrome, 48 px
│  48  ├─────────────────────────────────────────┤
│  o   │                                         │
│ 200  │           RouterOutlet                  │
│      │                                         │
│ ──── │                                         │
│ Ajustes (fila de pie del panel)                │
└──────┴─────────────────────────────────────────┘
```

**Spec visual del panel:**

- Riel compacto 48; panel abierto 200-220 (el actual 220 vale).
- Fila de navegación: alto 36-40, icono 16 + etiqueta Body, radio 4 en el
  hover; sin fondo en reposo.
- Indicador de selección: píldora vertical de acento 3×16 px en el borde
  izquierdo de la fila activa + texto primario; el resto texto secundario.
- "Ajustes" como fila de pie del panel, separada 8 px del grupo principal.
- Botón hamburguesa: solo con panel abierto (colapsa a riel); con riel no
  aparece (el panel se abre con hover/click sobre el riel en NavigationView;
  si se mantiene click-explicito, el botón vive en la fila de chrome, 48 px).
- Botón "Atrás": en la fila de chrome a la izquierda del título de página,
  32 px, solo icono; deshabilitado sin historial. Ya no dentro de un
  `DockPanel` propio del contenido.
- Mica se queda. Margen superior 32 bajo la barra de título se queda.

**Cambios:** eliminar el `chrome` DockPanel (`Program.fs:284-288`), integrar
atrás + hamburguesa en una fila única; estilizar filas por clase `nav-item`
con indicador de selección; mover "Ajustes" al pie del panel.

---

## 2. Login (`Views/Login.fs`)

**Contenido hoy:** título, campo de token enmascarado (TextBox con
`PasswordChar`, radio 0 — inconsistente), comando "Guardar" en el CommandBar
habilitado con texto, área de resultado: texto guardado / bloque rojo con texto
blanco + cuerpo rojo + botón "Continuar" / texto de error rojo. Medida 560.

**Patrón:** página de credencial de primer arranque = formulario enfocado.
WinUI no pone CommandBar aquí: un campo y una acción. El error de bóveda es un
InfoBar (banda de severidad con acción), no un bloque con texto invertido.

**Estructura:**

```
┌──────────────────────────────┐
│ Conectar proveedor (Title)   │
│                              │
│ Token de API                 │
│ [ ••••••••••••••••  👁 ]      │   ← TextBox 32, radio 4
│                              │
│ [ Conectar ]  (primario)     │   ← bajo el campo, izquierda
│                              │
│ (InfoBar de error si falla)  │
└──────────────────────────────┘
```

**Spec:**

- Columna única, máx 400-560, anclada arriba (o centrada verticalmente: elegir
  una, misma para el wizard).
- Campo: TextBox 32 de alto, radio 4, placeholder del resx actual.
- "Conectar": botón primario, deshabilitado con campo vacío (ya existe).
- InfoBar de error (patrón para toda la app): contenedor radio 4, fondo
  `AppErrorFill`, borde `AppError` 1 px, icono de alerta 16, título BodyStrong,
  cuerpo Caption, acción "Continuar igualmente" como botón texto dentro de la
  banda. Reemplaza el bloque rojo `insecureWarning` (`Login.fs:88-116`).
- "Token guardado" como Caption secundario bajo el botón, no como línea suelta.

**Cambios:** eliminar CommandBar; radio 4 en el TextBox; InfoBar en vez del
bloque rojo; primario en el cuerpo, no en el encabezado.

---

## 3. Atletas — lista (`Components/AthleteList.fs`)

**Contenido hoy:** título, comando "Nuevo atleta" (iconCommand), estado vacío
(glyph 32 + Caption), filas = `Button` Fluent con nombre 16 Bold + resumen en
`muted` fijo. Medida 760.

**Patrón:** página de lista WinUI: ListView de ítems de dos líneas, fila como
contenido (no botón), chevron, hover alfa. Acción de creación como primario
del encabezado.

**Estructura:**

```
┌──────────────────────────────────────────────┐
│ Atletas                          [+ Nuevo]   │   ← primario arriba derecha
│                                              │
│  Pancho                          ›           │   ← fila 64: BodyStrong + Caption
│    hombre · Hipertrofia, Fuerza              │
│  Talamandra                      ›           │
│    mujer · Pérdida de peso, Hipertrofia      │
└──────────────────────────────────────────────┘
```

**Spec de fila:**

- Fila 64: línea 1 `BodyStrong` (nombre), línea 2 `Caption` en
  `AppTextSecondary` (género · metas). Chevron 16 `AppTextTertiary` a la
  derecha. Toda la fila clicable; hover `AppCardFillHover` radio 4; selección
  visual no necesaria (navega al detalle).
- Separador entre filas: nada; el espaciado de 4 px agrupa (Fluent: el blanco
  agrupa, no la línea).
- Estado vacío: centrado horizontal, glyph 40 `AppTextTertiary` + Caption,
  vertical centrado en el cuerpo.

**Cambios:** filas dejan de ser botones; "Nuevo atleta" pasa a botón primario
en el encabezado (derecha); resumen con token secundario del tema.

---

## 4. Detalle de atleta (`Views/Athletes.fs`)

**Contenido hoy:** ScrollViewer propio (no usa la plantilla de página), nombre
como título, línea de perfil `muted` fija, botón primario "Generar plan",
Subtitle "Planes", filas de plan = `Button` (título 14 Bold + tipo · fecha
`muted`), Caption de vacío.

**Patrón:** página de detalle: encabezado con nombre, acción primaria del
encabezado, sección de lista con el patrón de filas de §3.

**Estructura:**

```
┌──────────────────────────────────────────────┐
│ Pancho                        [⚡ Generar plan]│   ← primario en encabezado
│ hombre · Hipertrofia, Fuerza (Caption)       │
│                                              │
│ Planes (Subtitle)                            │
│  Mixto · básicos                  ›          │
│    adjust · 16/10/2026 10:30                 │
└──────────────────────────────────────────────┘
```

**Spec:**

- Usar la plantilla de página §0.6 (hoy usa ScrollViewer propio, `Athletes.fs:140-157`).
- "Generar plan": primario del encabezado (con icono 16).
- Filas de plan: patrón §3 (dos líneas: título, tipo · fecha en Caption
  secundario, chevron).
- Vacío: Caption "sin planes" + sugerencia; sin sección vacía colgando.

**Cambios:** migrar a la plantilla; filas fuera de botones; primario al
encabezado.

---

## 5. Nuevo atleta (`Components/NewAthleteForm.fs`)

**Contenido hoy:** título "Nuevo atleta", "Guardar" en CommandBar (habilitado
por validez), cuerpo: nombre (TextBox), sexo (2 radios), metas (5 checkboxes),
cancelar como HyperlinkButton suelto. Medida 560.

**Patrón:** formulario WinUI de columna única: etiqueta encima del control,
grupos separados 16, validación en Caption bajo el campo, primario en el
encabezado.

**Estructura:**

```
┌──────────────────────────────┐
│ Nuevo atleta     [Cancelar] [Guardar]        │
│                                              │
│ Nombre                                       │
│ [___________]                                │
│                                              │
│ Sexo                                         │
│ (•) Hombre  ( ) Mujer                        │
│                                              │
│ Metas                                        │
│ [x] Hipertrofia   [ ] Pérdida de peso        │
│ [ ] Cardio        [ ] Activación             │
│ [ ] Fuerza                                   │
└──────────────────────────────────────────────┘
```

**Spec:**

- Etiqueta: Caption en `AppTextSecondary`, 4 px sobre el control (ya existe
  como `field`).
- Metas: 5 checkboxes en `WrapPanel` de dos columnas (o lista vertical si el
  texto rompe); el checked usa el estilo Fluent por defecto (sin relleno
  personalizado).
- "Guardar": primario del encabezado, deshabilitado hasta validez (ya existe).
- "Cancelar": botón estándar junto al primario, o texto en el cuerpo; no
  hipervínculo suelto al fondo.
- Validación: Caption en `AppError` bajo el campo que falta, solo tras el
  primer intento de guardado.

**Cambios:** CommandBar fuera; cancelar como botón estándar; radios y checks
sin estilo propio (el Fluent ya es coherente aquí).

---

## 6. Generar — wizard (`Views/Generate.fs` + `WizardBrief` + `MemoryReview`)

**Contenido hoy:** 5 pasos (Memorias → Brief → Generando → Fallido → Hecho)
con CrossFade 150 ms; footer ya en uso: Memorias = primario "Siguiente";
Brief = secundario "Atrás" PERO `WizardBrief` trae sus propios botones
Generar/Cancelar dentro del cuerpo (acción duplicada); Generando = barra
indeterminada 240 + Caption; Fallido = razones en líneas + reintentar/ salir;
Hecho = PlanReview + avisos + Confirmar/Desechar (con confirmación).

**Patrón:** wizard de escritura Fluent: encabezado con título + indicador de
paso, cuerpo, footer persistente con Atrás (izquierda) y acción primaria
(derecha). El footer actual es el patrón correcto — se conserva SOLO aquí
(§0.6). Los pasos de estado (Generando/Fallido) son estados del mismo wizard,
no páginas.

**Estructura:**

```
┌──────────────────────────────────────────────┐
│ Generar plan                                 │
│ Paso 1 de 2 · Memorias (Caption)             │
│                                              │
│  [tarjeta de memoria]                        │
│  [tarjeta de memoria]                        │
│                                              │
│ Atrás (texto)                     Siguiente (primario) │  ← footer
└──────────────────────────────────────────────┘
```

**Spec por paso:**

1. **Memorias.** Cada propuesta = tarjeta §0.3: origen Body, deltas como
   checkboxes, alcance como radios en línea, acciones Confirmar (primario,
   dentro de la tarjeta, pequeño) y Desechar (texto). Las confirmadas que
   viajan: lista de lectura (Caption) bajo un Subtitle "Se aplican", no
   mezcladas con tarjetas. "Siguiente" deshabilitado hasta decidir todas (ya
   existe). Tarjetas en `ItemsControl` con espaciado 8.
2. **Brief.** Un solo lugar para acciones: el footer lleva Atrás (secundario)
   y Generar (primario). `WizardBrief` deja de renderizar sus propios botones
   (`WizardBrief.fs:288-300,310-313` se eliminan). Grupos como formulario §5:
   Punto de partida (radios), Semanas (radios 1-4 en línea), Objetivo (radios),
   Frecuencia (radios 2-6), Énfasis (checkboxes del catálogo, WrapPanel en 2-3
   columnas — hasta 24 elementos en línea única no caben).
3. **Generando.** Centrado vertical: `ProgressRing` 40 (no ProgressBar: la
   espera es modal-ish y el ring es el patrón Fluent para espera
   indeterminada), Caption del estado debajo, "Cancelar generación" como botón
   estándar bajo el ring (o en footer secundario — elegir uno; footer).
4. **Fallido.** Cada razón como línea Caption en `AppError` dentro de una
   tarjeta con fondo `AppErrorFill` si hay más de una; footer: Salir
   (estándar) + Reintentar (primario). Reintentar reenvía el último brief (ya
   existe).
5. **Hecho.** Subtitle "Revisa el plan"; cuerpo = PlanReview en modo lectura
   (§7); avisos como líneas Caption `AppError` bajo el preview; footer
   Desechar (estándar, con diálogo de confirmación — ya existe) + Confirmar
   (primario).

**Cambios:** indicador de paso Caption bajo el título; botones de
`WizardBrief` al footer; ring en vez de barra; tarjetas de memoria con el
contenedor §0.3.

---

## 7. Revisión del plan (`Views/Review.fs` + `PlanReview` + `NoteSection`)

La vista más cargada y la que más se parece al PWA. Aquí manda la disposición
del PWA: columnas por día, sesión como texto jerarquizado, acento solo en
selección.

**Contenido hoy:** título; "Guardar cambios" y "Exportar .briple" como
iconCommands en CommandBar de ancho completo; Caption de estado; Caption "sin
guardar" condicional; `PlanReview` (TabControl de semanas, secciones por
variante con badge acento-sobre-pastel, días apilados verticalmente, línea de
ejercicio con botón "Editar" por fila, edición = 4 TextBox en línea + Hecho/
Revertir); `NoteSection` (TextBox de una línea, botón "Interpretar" primario,
Caption de estado, tarjetas de propuesta). Medida 760.

**Patrón:** página de lectura con modo edición; el plan se presenta como el
PWA: selector de semana segmentado + columnas horizontales por día; la edición
es un estado de página, no un botón por línea.

**Estructura:**

```
┌──────────────────────────────────────────────────────────────┐
│ Revisión del plan · Mixto · básicos     [Exportar] [💾 Guardar]│
│ ● Cambios sin guardar (Caption, solo si aplica)               │
│                                                               │
│ Semana 1   Semana 2   Semana 3   Semana 4   ← segmentado      │
│ ───────                                                       │
│ ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌───────────┐       │
│ │ lunes · Dí│1│ martes · …│ │ miércoles │ │ viernes   │  →    │
│ │ (Subtitle)│ │           │ │           │ │           │  scroll│
│ │ ▎3 vueltas│ │           │ │           │ │           │  horiz.│
│ │           │ │           │ │           │ │           │       │
│ │ Sentadilla│ │           │ │           │ │           │       │
│ │ goblet    │ │           │ │           │ │           │       │
│ │ 10-12 · RIR│ │          │ │           │ │           │       │
│ │ 3 · 45 s  │ │           │ │           │ │           │       │
│ └───────────┘ └───────────┘ └───────────┘ └───────────┘       │
│                                                               │
│ Nota de sesión (Subtitle)                                     │
│ [_________________________________] [Interpretar]             │
│ (Caption de estado)                                           │
│ [tarjeta de propuesta]                                        │
└──────────────────────────────────────────────────────────────┘
```

**Spec:**

1. **Encabezado.** Título + subtítulo del plan (Caption secundario bajo el
   título, no en el Title). "Guardar cambios" = primario del encabezado,
   deshabilitado sin cambios pendientes (sustituye el Caption "sin guardar");
   "Exportar" = estándar a su izquierda. Sin CommandBar.
2. **Selector de semana.** Fila segmentada: texto Subtitle por semana, la
   activa en `AppTextPrimary` con subrayado de acento de 2-3 px, las inactivas
   `AppTextSecondary`. Implementación: `TabControl` re-estilizado o
   `ItemsControl` + estado de selección (decidir en implementación; el
   lazy-build actual se conserva).
3. **Columnas por día** (el cambio estructural grande, viene del PWA):
   `ScrollViewer` horizontal con `ItemsControl` horizontal; una tarjeta §0.3
   por día, ancho fijo 300-340, alineada arriba; los días de descanso como
   tarjeta atenuada con Caption "Descanso · próxima: lunes, …".
   - Cabecera de tarjeta: `día — título` en BodyStrong, 12 px debajo.
   - Circuitos: etiqueta Caption en mayúsculas `AppTextSecondary`
     ("A × 3 vueltas"), 8 px de separación.
   - Ejercicio: línea 1 Body (nombre), línea 2 Caption secundario
     (`10-12 · RIR 3 · 45 s · nota`). Sin numeración (el orden es visual);
     la numeración vuelve en modo edición si hace falta.
4. **Modo edición.** Botón estándar "Editar" en el encabezado (tercero,
   izquierda de Exportar); activo → "Listo" (primario se mantiene Guardar).
   En modo edición, cada ejercicio de la tarjeta activa muestra su fila de
   campos existente (los 4 TextBox + Hecho/Revertir por fila ya construidos)
   dentro de la tarjeta; fuera de modo edición no hay ningún botón "Editar"
   por línea. El `iconButton` por fila (`PlanReview.fs:179-185`) desaparece.
   El badge "Semana 1 · RIR 3" → Caption en `AppAccent` sin fondo, junto a la
   cabecera de la variante (el pastel `accentTonal` desaparece).
5. **Nota de sesión.** Sección al pie del cuerpo (bajo las columnas, ancho
   completo del cuerpo): Subtitle "Nota de sesión", TextBox multilínea
   (`AcceptsReturn`, 2-3 líneas) + "Interpretar" primario pequeño a la derecha
   del campo; Caption de estado con tono (ya existe la lógica); propuestas =
   tarjetas §6.1. Tope de 10 memorias: cuando la superficie está llena,
   InfoBar informativa en vez de error de texto.
6. **Estado vacío de guardado.** El Caption de estado (última operación) vive
   junto a los botones del encabezado o bajo ellos, alineado a la derecha.

**Cambios:** CommandBar fuera; columnas por día en horizontal; filas de
ejercicio sin botón; edición por modo de página; badge sin pastel; nota con
campo multilínea; guardar como primario habilitado por estado.

---

## 8. Ajustes (`Views/Settings.fs`)

**Contenido hoy:** 3 campos sueltos: idioma (2 radios), tema (3 radios),
proveedor (Caption conectado / Caption falta token + primario "Conectar").
Medida 560.

**Patrón:** settings-cards de Windows 11: filas agrupadas en tarjetas, título
+ descripción a la izquierda, control a la derecha, una tarjeta por ajusto,
grupos separados por Subtitle.

**Estructura:**

```
┌──────────────────────────────────────────────┐
│ Ajustes                                      │
│                                              │
│ Preferencias (Subtitle)                      │
│ ┌──────────────────────────────────────────┐ │
│ │ Idioma                    [Español ▾]    │ │
│ ├──────────────────────────────────────────┤ │
│ │ Tema                      [Sistema  ▾]   │ │
│ └──────────────────────────────────────────┘ │
│                                              │
│ Proveedor (Subtitle)                         │
│ ┌──────────────────────────────────────────┐ │
│ │ DeepSeek     Conectado    [Desconectar]  │ │
│ └──────────────────────────────────────────┘ │
└──────────────────────────────────────────────┘
```

**Spec:**

- Tarjeta-fila: alto 56-64, padding 16, título Body a la izquierda, control
  alineado a la derecha; tarjetas del mismo grupo pegadas (radio solo en el
  grupo completo: primero/último 4 px, estilo Win11) o separadas 4 — elegir
  pegadas.
- Idioma y tema: `ComboBox` a la derecha (2-3 opciones fijas; el radio group
  inline también es válido, pero el card-pattern de Win11 usa ComboBox).
- Proveedor: estado en Caption (Conectado/No conectado) + acción derecha:
  "Conectar" primario pequeño, o "Cambiar token" estándar cuando ya hay.
- Sin pie de página; cada cambio aplica inmediato (ya es así).

**Cambios:** de pila de campos a tarjetas agrupadas; radios → ComboBox
derecho; proveedor con estado + acción.

---

## 9. Diálogos (`Dialogs.fs`)

**Contenido hoy:** overlay propio sobre `Dialogs.host`; scrim `#00000099`,
panel `#202020`/blanco radio 8 padding 24 máx 440, título 20 SemiBold, cuerpo,
botones Cancelar + Aceptar ambos estilo por defecto; Escape cancela.

**Patrón:** ContentDialog (WinUI). La estructura ya es correcta; faltan dos
detalles canónicos.

**Spec:**

- Panel: fondo sólido de capa (`AppCardFill` sobre un base sólido — en la
  práctica un gris sólido por tema: `#2C2C2C` dark / `#FFFFFF` light), radio 8
  (Windows 11), sombra suave permitida en diálogos (elevación), máx 440.
- Scrim: `#66000000` dark, `#33000000` light (más suave que el actual).
- Botones abajo derecha: secundario (Cancelar) estándar; primario (Aceptar)
  relleno de acento. Hoy ambos salen neutros y la acción destructiva no se
  distingue: `ConfirmRequest` gana un campo `Destructive: bool` (o el primario
  se declara en la request) — la acción destructiva lleva el tono de error.
- El botón por defecto responde a Enter; Escape ya funciona.
- El cuerpo usa Caption/Body; sin cambios.

**Cambios:** primario con acento (o tono de error si destructivo); scrim por
tema; fondo de panel por tema vía tokens (hoy está a mano en
`cardBackground`).

---

## 10. Toasts (`Toasts.fs`)

**Contenido hoy:** `WindowNotificationManager` abajo-derecha, máx 3, tipos
Information/Error.

**Patrón:** toast de Windows 11: tarjeta 8 de radio, icono de severidad,
título BodyStrong + cuerpo Caption, fondo sólido de capa, auto-despedida.

**Spec:** mantener el mecanismo; con la capa de estilos, el template del
notification manager toma los tokens del tema (hoy hereda Fluent y funciona;
no requiere obra). Único ajuste: errores con icono de error y título del
action (ya viaja), éxitos con icono de información. Prioridad baja.

---

## 11. Anti-patrones a eliminar (lista única)

1. CommandBar de ancho completo bajo el título (`Fields.fs:commandBar` /
   `fluentPage`) → comandos en el encabezado.
2. Footer en páginas de lectura (`fluentPage` Footer) → solo wizards.
3. Filas que son `Button` (atletas, planes) → filas de contenido.
4. Botón "Editar" por línea de ejercicio → modo edición de página.
5. Pinceles hex inline en vistas (`Theme.fs` consumido directo) → tokens por
   tema.
6. Chips con relleno de acento/gris (`Fields.fs:chipButton` y variantes
   reactivas) → radios/checkboxes Fluent o píldora con borde de acento.
7. Bloque rojo con texto blanco (Login) → InfoBar.
8. Badge pastel `accentTonal` (PlanReview) → Caption de acento sin fondo.
9. `CornerRadius(0)` en el TextBox de Login → radio 4 global.
10. Título + comando separados en filas distintas → fila de encabezado única.
11. Botón "Atrás" en DockPanel propio del contenido → chrome del shell.
12. Doble acción en el paso Brief (footer Atrás + Generar/Cancelar dentro del
    cuerpo) → acciones solo en el footer.

## 12. Decisiones abiertas (bloquean implementación, no el spec)

1. **Shell:** (a) SplitView actual pulido al spec NavigationView, (b) control
   de navegación propio del repo, (c) FluentAvalonia (dependencia nueva; trae
   NavigationView y ContentDialog fieles, pero impone su theme). Recomendación:
   (b) — un control `Components/NavPane` propio con el spec §1; (c) solo si se
   quiere cerrar NavigationView + ContentDialog + InfoBar de un golpe.
2. **Selector de semana:** re-estilizar `TabControl` vs `ItemsControl` con
   estado propio. Recomendación: `ItemsControl` + estado (el lazy-build ya
   existe y el TabControl no aporta nada más).
3. **Master-details de atletas** (lista izquierda, detalle derecha en ventana
   ancha): posponer; la navegación actual es suficiente.
4. **Cadena de textos nueva requerida:** indicador de paso del wizard
   ("Paso {0} de {1}"), modo edición ("Editar"/"Listo"), "Descanso · próxima:
   {0}" en tarjeta de día de descanso. Todo lo demás reusa claves existentes.

## 13. Orden de implementación sugerido

1. Tokens por tema + rampa + estilos de botón/fila/tarjeta (§0) — una vez.
2. Plantilla de página (§0.6) y migración trivial de vistas: Ajustes (§8),
   Nuevo atleta (§5), Atletas (§3), Detalle (§4).
3. Login (§2) + InfoBar.
4. Shell (§1).
5. Wizard (§6).
6. Revisión (§7) — la más grande, al final con todo el vocabulario listo.

Cada paso termina con captura de la ventana comparada contra este spec;
el paso no cierra con tests en verde sino también con la captura revisada.
