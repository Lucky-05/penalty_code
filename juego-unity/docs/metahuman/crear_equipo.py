# Crea al tirador y al portero como dos MetaHumans distintos (cuerpo masculino, cara y tono
# de piel propios), les genera esqueleto y texturas en la nube de Epic, y exporta mapas y
# mallas en FBX para Unity.
#   UnrealEditor PenalesMetaHuman.uproject -ExecutePythonScript="Scripts/crear_equipo.py"
import os
import random
import traceback
import unreal

SALIDA = os.path.expanduser("~/penales/ArteFuente/MetaHuman")
BITACORA = os.path.join(SALIDA, "_creacion.log")
RUTA = "/Game/Jugadores"

# Por defecto, tirador y portero. Con PENALES_BASES="Kelvin,Victor" se generan esos personajes
# predefinidos con su propio nombre (sirve para comparar candidatos).
_bases = [b.strip() for b in os.environ.get("PENALES_BASES", "").split(",") if b.strip()]
JUGADORES = ([{"nombre": b.replace("-", ""), "base": b} for b in _bases] if _bases else [
    {"nombre": "Tirador", "base": "Mateo"},
    {"nombre": "Portero", "base": "Omari"},
])


def anotar(texto):
    unreal.log("[Penales] " + texto)
    with open(BITACORA, "a", encoding="utf-8") as f:
        f.write(texto + "\n")


def intento(etiqueta, fn):
    """Cada ajuste es opcional: si uno falla, se anota y el personaje sigue adelante."""
    try:
        fn()
        anotar("  ok: " + etiqueta)
    except Exception as e:
        anotar("  FALLÓ %s: %s" % (etiqueta, str(e)[:300]))


def a_fbx(malla, archivo):
    op = unreal.FbxExportOption()
    for k, v in (("collision", False), ("level_of_detail", False), ("vertex_color", False),
                 ("export_morph_targets", True), ("ascii", False)):
        op.set_editor_property(k, v)
    t = unreal.AssetExportTask()
    for k, v in (("object", malla), ("filename", archivo), ("automated", True), ("prompt", False),
                 ("replace_identical", True), ("options", op)):
        t.set_editor_property(k, v)
    ok = unreal.Exporter.run_asset_export_task(t)
    tam = os.path.getsize(archivo) / 1048576.0 if os.path.exists(archivo) else 0
    anotar("  %s (%.1f MB) %s" % (os.path.basename(archivo), tam, "ok" if ok else "FALLÓ"))


def crear(j):
    nombre = j["nombre"]
    completo = "%s/%s.%s" % (RUTA, nombre, nombre)
    if unreal.EditorAssetLibrary.does_asset_exist(completo):
        unreal.EditorAssetLibrary.delete_asset(completo)
    # Se parte de un personaje completo que ya trae MetaHuman (cara, cuerpo y piel propios).
    base = "/MetaHumanCharacter/Optional/Presets/%s" % j["base"]
    if not unreal.EditorAssetLibrary.does_asset_exist(base):
        raise RuntimeError("no existe el personaje predefinido " + base)
    p = unreal.EditorAssetLibrary.duplicate_asset(base, "%s/%s" % (RUTA, nombre))
    if p is None:
        raise RuntimeError("no se pudo copiar %s" % j["base"])
    anotar("%s: creado a partir de %s" % (nombre, j["base"]))

    sub = unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
    if not sub.try_add_object_to_edit(character=p):
        raise RuntimeError("no se pudo abrir %s para editar" % nombre)
    try:
        rig = unreal.MetaHumanCharacterAutoRiggingRequestParams()
        rig.blocking = True
        rig.report_progress = False
        rig.rig_type = unreal.MetaHumanRigType.JOINTS_ONLY
        anotar("%s: pidiendo esqueleto…" % nombre)
        sub.request_auto_rigging(character=p, params=rig)

        tex = unreal.MetaHumanCharacterTextureRequestParams()
        tex.blocking = True
        tex.report_progress = False
        anotar("%s: pidiendo texturas…" % nombre)
        sub.request_texture_sources(character=p, params=tex)
        anotar("%s: texturas de alta resolución = %s" % (nombre, p.has_high_resolution_textures))
        unreal.EditorAssetLibrary.save_loaded_asset(p)

        destino = os.path.join(SALIDA, nombre)
        os.makedirs(destino, exist_ok=True)
        d = unreal.MetaHumanDCCExportParams()
        d.external_path = destino
        d.bake_make_up = False
        d.compress_in_zip_file = False
        anotar("%s: exportando mapas…" % nombre)
        unreal.MetaHumanCharacterExportBlueprintLibrary.export_dcc(p, d)

        ruta = "/Game/Exportado/%s" % nombre
        g = unreal.MetaHumanGeometryExportParams()
        g.project_path = ruta
        g.head_skeletal_mesh = True
        g.body_skeletal_mesh = True
        g.full_body_skeletal_mesh = False
        g.overwrite_existing_assets = True
        anotar("%s: generando geometría…" % nombre)
        unreal.MetaHumanCharacterExportBlueprintLibrary.export_geometry(p, g)
    finally:
        if sub.is_object_added_for_editing(character=p):
            sub.remove_object_to_edit(character=p)

    mallas = os.path.join(SALIDA, nombre, "Malla")
    os.makedirs(mallas, exist_ok=True)
    for a in unreal.EditorAssetLibrary.list_assets("/Game/Exportado/%s" % nombre, recursive=True, include_folder=False):
        obj = unreal.load_asset(a)
        if isinstance(obj, unreal.SkeletalMesh):
            a_fbx(obj, os.path.join(mallas, obj.get_name() + ".fbx"))
    n = sum(len(fs) for _, _, fs in os.walk(os.path.join(SALIDA, nombre)))
    anotar("%s: LISTO, %d archivos" % (nombre, n))


os.makedirs(SALIDA, exist_ok=True)
anotar("=== equipo: %s ===" % ", ".join(j["nombre"] for j in JUGADORES))
for j in JUGADORES:
    try:
        crear(j)
    except Exception as e:
        anotar("ERROR en %s: %s" % (j["nombre"], e))
        anotar(traceback.format_exc())
anotar("=== fin ===")
