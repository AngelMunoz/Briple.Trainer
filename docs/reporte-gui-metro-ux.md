# Reporte GUI — patrones Metro y reglas de UX

Fecha: 2026-10-16.
Base: `main` (commit `29ce2c4`), `src/Briple.Trainer/` completo.
Skills: `metro-design` (con `references/controls.md`) y `ui-design-rules`.
Alcance: patrones de navegación, estructura visual, layout, padding,
app bar, títulos. Los tokens exactos (colores, pesos, tamaños) quedan
fuera, según lo pedido. Sin cambios de código.

## Veredicto

El GUI expresa bien el Metro de página: plano, tipográfico, estados como
contenido, un acento, pivote de semanas. No expresa el Metro de
aplicación: no hay contenedor de navegación (Panorama/Pivot/Hub) como
home, el app bar es persistente y mezcla navegación con settings, no hay
camino de vuelta en la página de revisión, y la rampa tipográfica colapsó
a un solo nivel de título. Hay cuatro hallazgos UX de gravedad: un botón
"Atrás" que cancela todo el wizard, el toggle de idioma que reinicia la
navegación, un fallo de guardado silencioso, y la revisión sin salida.

## 1. Lo que ya es Metro (patrón, no token)

- **Sin barra superior, sin hamburger.** El shell es un `DockPanel` con
  el bar abajo y el resto rutas (`Program.fs:97-99`). La regla "si
  aparece una barra arriba, la estructura está mal" se cumple.
- **Contenido antes que chrome.** Sin bordes, tarjetas ni sombras.
  `CornerRadius(0.0)` en botones y entradas (`Fields.fs:33,146`). El plan
  se lee como prosa: "1. Press de banca · 8-10 · RIR 2 · 120s"
  (`PlanReview.fs:149-160`).
- **Estados como contenido, no popups.** Vacío de atletas y de memorias
  como `label` inline (`AthleteList.fs:107-109`, `Generate.fs:471-475`);
  errores de validación como líneas rojas en la página
  (`Generate.fs:515-529`); progreso indeterminado inline junto al
  contenido (`Generate.fs:506-513`). Cero `ContentDialog` en la app.
- **Pivote real dentro del plan.** Las semanas son secciones pares de un
  mismo dato, una visible a la vez, header seleccionable, contenido que
  se re-renderiza (`PlanReview.fs:369-413`). El grupo de radios da
  cambio con flechas del teclado, como pide Metro.
- **Wizard como máquina de pasos** (linaje FlipView): Memories → Brief →
  Generating → Failed/Done, con numeración "1./2." en los títulos
  (`Generate.fs:217-222`).
- **Disciplina de acento.** Un solo acento (`Theme.fs:5`), usado en
  botón primario, chip encendido, badge de semana y texto del botón home:
  todos interactivos o de selección. Nada de acento decorativo.
- **Fila completa activable.** Atleta y plan son botones de ancho
  completo con contenido a la izquierda (`AthleteList.fs:58-76`,
  `Athletes.fs:61-78`): buen objetivo de clic, reconocimiento antes que
  recuerdo.
- **Padding consistente.** Margen 24 y espaciados 8/12 en todas las
  páginas; 24 está en la escala Metro (4/8/12/16/24/40).

## 2. Desviaciones de patrón Metro (estructura, aplicables con código nuevo)

1. **El home es una página plana con un botón.** Metro exige Panorama,
   Pivot o Hub como home, nunca "página + menú"
   (`references/controls.md`, "App structure patterns"). `/` es título +
   lista + "Nuevo atleta" (`AthleteList.fs:107-118`). Para la escala de
   la app un Hub completo es discutible; la opción honesta más cercana es
   un Pivot con "Atletas" / actividad reciente. Hoy no existe ninguna
   superficie de paneo horizontal en el código.
2. **El app bar mezcla roles y no se invoca.** Regla Metro: el bar lleva
   comandos; los enlaces a páginas viven en el menú de ellipsis (⋯); los
   settings en SettingsFlyout o el menú. Hoy: botón "Atletas" (navegación)
   a la izquierda y toggle de idioma (setting) a la derecha, siempre
   visibles (`AppBar.fs:32-50`, `Program.fs:82-99`). En W8 el bar se
   oculta y se invoca con clic derecho o swipe; aquí está dockeado fijo.
   No hay ellipsis, ni MenuFlyout, ni comandos que despidan el bar.
3. **No hay pila de retroceso.** Navs no guarda historial; "atrás" es un
   `Navigate` hardcodeado por página (`Routes.fsi:3-6`). Metro devuelve
   con back del sistema o chevron en pantalla. No existe ni chevron ni
   back; ver hallazgo UX-1 para el caso peor.
4. **No hay superficie de settings.** El idioma es la única preferencia y
   vive en el bar. Sin SettingsFlyout ni página de settings, el patrón
   Metro de settings no se puede expresar hoy.
5. **Un solo tema, claro.** Metro nace oscuro con par claro/invertido.
   `Theme.fs` es una paleta clara única y `FluentTheme` no recibe
   variante (`Program.fs:33`). Es una decisión estructural de theming,
   no un token suelto.
6. **La rampa tipográfica colapsó.** Existe `title` (22 negrita) y
   `label` (12 muted) como únicos niveles (`Fields.fs:11-20`); los demás
   tamaños son literales en cada vista (20/16/14). Consecuencia
   estructural: en la ficha del atleta, "Planes" (sección) usa el mismo
   `title` que el nombre del atleta (página) (`Athletes.fs:145,152`). No
   hay fábrica de header de sección, así que la jerarquía página >
   sección no se puede expresar sin duplicar código.
7. **Cero vocabulario de movimiento.** Sin transiciones de navegación
   (turnstile), sin slide de pivote, sin stagger. Los cambios de contenido
   son instantáneos. "Fast and fluid" queda en instantáneo; no rompe,
   pero el patrón de motion no existe en el código.
8. **Objetivos táctiles bajo 34px.** El botón base usa `Padding(12,6)`
   (`Fields.fs:34`): ~30px de alto, menos en el bar (sin padding propio).
   Metro pide ≥34px con 8px de separación.
9. **La fábrica `page` es demasiado rígida.** Firma `title + content`
   (`Fields.fs:191-195`). Las páginas con subtítulo o acciones entre
   título y contenido la copian a mano: `Athletes.fs:139-143`,
   `Generate.fs:583-586`, `Review.fs:205-209`. Los valores coinciden hoy
   (24/8); el mecanismo es frágil ante deriva. Es el lugar natural para
   fijar el contrato de "página Metro" (margen, ancho de lectura, nivel
   de título) si se quiere endurecer.

## 3. Hallazgos UX (ui-design-rules), por gravedad

### P1

- **UX-1: la revisión del plan no tiene salida.** La página lleva título,
  acciones, plan y notas (`Review.fs:205-220`). Sin botón atrás y sin
  historial, la única salida es "Atletas" en el bar, que va a `/` y
  pierde el contexto del atleta (`Program.fs:87-89`). Para volver al
  atleta hay que reencontrarlo en la lista. Viola "camino claro de
  vuelta" y "preservar contexto".
- **UX-2: "Atrás" que cancela el wizard completo.** En el paso Failed el
  botón usa la clave `wizardBack` pero despacha `Cancelled`
  (`Generate.fs:528`), que navega a la ficha del atleta y mata la
  generación en curso (`Generate.fs:423-432`). El usuario lee "Atrás" y
  espera volver al Brief; pierde todo. Etiqueta ≠ acción.
- **UX-3: el toggle de idioma reinicia la navegación.** Cambiar idioma
  hace `SetLanguage` y `go(router, "/")` desde cualquier página
  (`Program.fs:90-94`). En revisión o wizard, el cambio de idioma expulsa
  a la lista. Pérdida de contexto evitable.
- **UX-4: fallo de guardado silencioso.** `NewAthleteForm.Saved` con
  `Error` solo hace `Log.Error` (`Routes.fs:74`); el formulario no
  informa nada y se queda como está. El usuario no sabe si guardó. La
  regla: los errores deben decir qué falló y cómo recuperar.

### P2

- **UX-5: título de página y de sección idénticos.** Mismo `title` para
  el nombre del atleta y para "Planes" (`Athletes.fs:145,152`). Dos
  niveles de la jerarquía ocupan un nivel visual. Falta el nivel
  "header de sección".
- **UX-6: sin ancho de lectura fuera de login.** Solo login acota
  `MaxWidth(560)` (`Login.fs:179`). Lista, ficha, wizard y revisión
  estiran las líneas al ancho de la ventana (1100px por defecto). Prosa
  y formularios a ~1050px por línea cansan la lectura.
- **UX-7: "Descartar" sin confirmación y con costo alto.** Descartar el
  plan generado tira 1-2 minutos de generación y despide el wizard
  (`Generate.fs:559`). Es destructivo y caro: la regla pide confirmación
  exactamente aquí. Es el único candidato legítimo a `MessageDialog` en
  toda la app.
- **UX-8: mensajes de excepción crudos al usuario.** `e.Message` llega a
  pantalla en fallo de login (`Login.fs:124-129`), fallo de generación
  (`Generate.fs:354-356`) y fallo de exportación (`Review.fs:88-90,
  133-139`). Texto técnico, a menudo en inglés, sin acción siguiente.
- **UX-9: no se puede cancelar una generación en curso.** En Generating
  no hay botón: "nothing else to press while it works"
  (`Generate.fs:503-513`). El CTS existe y se cancela al salir, pero el
  único modo de parar es abandonar el wizard. La regla de control del
  usuario pide un cancel explícito para esperas de minutos.
- **UX-10: el login bloquea el uso sin agente.** La ruta inicial es
  `/login` cuando no hay token (`Routes.fs:196-200`). El CRUD de atletas
  no necesita al agente; un usuario sin token no puede registrar ni uno.
  Decisión de producto, pero conviene saber que el gate es total.

### P3

- **UX-11: espaciados fuera de la grilla de 4px.** `Spacing(6)` en chips
  y tarjetas (`Fields.fs:60`, `MemoryReview.fs:159`, `NoteSection.fs:293`),
  `Spacing(2)` en filas (`AthleteList.fs:63`, `Athletes.fs:69`). 2 y 6 no
  están en la escala 4/8/12/16/24/40.
- **UX-12: botón home autoreferente.** En `/` el bar muestra "Atletas"
  cuando ya estás en Atletas (`AppBar.fs:33-35`). Sin estado
  deshabilitado ni diferenciado.
- **UX-13: filas de edición con anchos fijos.** Las entradas de ejercicio
  usan 90/60/90/260 (`PlanReview.fs:214-217`). En ventanas angostas la
  fila horizontal puede desbordar; el resto de la app refluye en
  columna.
- **UX-14: "Siguiente" deshabilitado sin explicación.** Se habilita al
  decidir todas las tarjetas (`Generate.fs:452-457`); la condición es
  invisible para el usuario nuevo.

## 4. Capacidad de expresión: patrón Metro vs. código actual

| Patrón Metro | Estado hoy |
| --- | --- |
| Página plana tipográfica, margen 24 | Expresado (`Fields.page`) |
| Pivoto (secciones pares) | Expresado (semanas, `PlanReview`) |
| Estados inline (vacío/error/progreso) | Expresado |
| Acento único disciplinado | Expresado (`Theme`) |
| Fila completa activable | Expresado |
| App bar invocable (W8) / que se esconde al scroll (WP) | Ausente; bar dockeado fijo |
| Botones de icono con etiqueta + ellipsis (⋯) | Ausente; solo texto |
| MenuFlyout / SettingsFlyout | Ausente |
| Home Panorama/Pivot/Hub | Ausente; página plana |
| Pila de retroceso / chevron | Ausente; Navs sin historial |
| Par de temas oscuro/claro | Ausente; solo claro |
| Rampa tipográfica con niveles página/sección | Parcial; un solo `title` |
| FlipView con indicador de posición | Parcial; pasos numerados solo en título |
| Motion (turnstile/slide/stagger) | Ausente |
| Objetivos ≥34px | Ausente; botón base ~30px |

## 5. Nota de método

Lectura completa de `Program.fs`, `Theme.fs`, `Components/*` (AppBar,
Fields, AthleteList, NewAthleteForm, MemoryReview, NoteSection,
PlanReview, WizardBrief) y `Views/*` (Routes, Athletes, Generate, Login,
Review), más `references/controls.md` del skill Metro y los valores de
títulos en `Strings.resx`. No se ejecutó la app ni se modificó código.
