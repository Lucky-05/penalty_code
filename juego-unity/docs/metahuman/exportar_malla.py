# Saca la geometría del MetaHuman (cabeza, cuerpo y cuerpo completo) como mallas con esqueleto
# dentro del proyecto de Unreal y las exporta a FBX para Unity.
#   UnrealEditor PenalesMetaHuman.uproject -ExecutePythonScript="Scripts/exportar_malla.py"
import os
import traceback
import unreal

NOMBRES = os.environ.get("PENALES_JUGADORES", "Tirador").split(",")
SALIDA = os.path.expanduser("~/penales/ArteFuente/MetaHuman")
BITACORA = os.path.join(SALIDA, "_creacion.log")


def anotar(texto):
    unreal.log("[Penales] " + texto)
    with open(BITACORA, "a", encoding="utf-8") as f:
        f.write(texto + "\n")


def a_fbx(ruta_asset, archivo):
    malla = unreal.load_asset(ruta_asset)
    if malla is None:
        anotar("  no existe " + ruta_asset)
        return False
    op = unreal.FbxExportOption()
    op.set_editor_property("collision", False)
    op.set_editor_property("level_of_detail", False)
    op.set_editor_property("vertex_color", False)
    op.set_editor_property("export_morph_targets", True)
    op.set_editor_property("ascii", False)
    t = unreal.AssetExportTask()
    t.set_editor_property("object", malla)
    t.set_editor_property("filename", archivo)
    t.set_editor_property("automated", True)
    t.set_editor_property("prompt", False)
    t.set_editor_property("replace_identical", True)
    t.set_editor_property("options", op)
    ok = unreal.Exporter.run_asset_export_task(t)
    tam = os.path.getsize(archivo) / 1048576.0 if os.path.exists(archivo) else 0
    anotar("  %s → %s (%.1f MB) %s" % (ruta_asset.split("/")[-1].split(".")[0], os.path.basename(archivo), tam, "ok" if ok else "FALLÓ"))
    return ok


def exportar(nombre):
    personaje = unreal.load_asset("/Game/Jugadores/%s.%s" % (nombre, nombre))
    if personaje is None:
        raise RuntimeError("no existe el personaje " + nombre)
    sub = unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
    if not sub.try_add_object_to_edit(character=personaje):
        raise RuntimeError("no se pudo abrir %s para editar" % nombre)
    ruta = "/Game/Exportado/%s" % nombre
    try:
        p = unreal.MetaHumanGeometryExportParams()
        p.project_path = ruta
        p.head_skeletal_mesh = True
        p.body_skeletal_mesh = True
        p.full_body_skeletal_mesh = True
        p.overwrite_existing_assets = True
        anotar("%s: generando geometría…" % nombre)
        unreal.MetaHumanCharacterExportBlueprintLibrary.export_geometry(personaje, p)
    finally:
        if sub.is_object_added_for_editing(character=personaje):
            sub.remove_object_to_edit(character=personaje)

    encontrados = unreal.EditorAssetLibrary.list_assets(ruta, recursive=True, include_folder=False)
    anotar("%s: assets generados: %s" % (nombre, ", ".join(a.split("/")[-1].split(".")[0] for a in encontrados)))
    destino = os.path.join(SALIDA, nombre, "Malla")
    os.makedirs(destino, exist_ok=True)
    for a in encontrados:
        obj = unreal.load_asset(a)
        if isinstance(obj, unreal.SkeletalMesh):
            a_fbx(a, os.path.join(destino, obj.get_name() + ".fbx"))


os.makedirs(SALIDA, exist_ok=True)
anotar("=== malla: %s ===" % ", ".join(NOMBRES))
for n in NOMBRES:
    try:
        exportar(n.strip())
    except Exception as e:
        anotar("ERROR en %s: %s" % (n, e))
        anotar(traceback.format_exc())
anotar("=== fin ===")
