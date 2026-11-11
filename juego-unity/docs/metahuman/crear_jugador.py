# Crea un MetaHuman, le pide a los servicios de Epic el esqueleto y las texturas, y lo exporta
# en formato para otras aplicaciones (FBX + texturas) hacia el proyecto de Unity.
#   UnrealEditor PenalesMetaHuman.uproject -ExecutePythonScript="Scripts/crear_jugador.py"
import os
import traceback
import unreal

NOMBRES = os.environ.get("PENALES_JUGADORES", "Tirador").split(",")
SALIDA = os.path.expanduser("~/penales/ArteFuente/MetaHuman")
BITACORA = os.path.join(SALIDA, "_creacion.log")
RUTA = "/Game/Jugadores"


def anotar(texto):
    unreal.log("[Penales] " + texto)
    with open(BITACORA, "a", encoding="utf-8") as f:
        f.write(texto + "\n")


def crear(nombre):
    completo = "%s/%s.%s" % (RUTA, nombre, nombre)
    if unreal.EditorAssetLibrary.does_asset_exist(completo):
        personaje = unreal.load_asset(completo)
        anotar("%s: ya existía, se reutiliza" % nombre)
    else:
        herramientas = unreal.AssetToolsHelpers.get_asset_tools()
        personaje = herramientas.create_asset(
            asset_name=nombre, package_path=RUTA, asset_class=unreal.MetaHumanCharacter,
            factory=unreal.new_object(type=unreal.MetaHumanCharacterFactoryNew))
        anotar("%s: creado" % nombre)
    if personaje is None:
        raise RuntimeError("no se pudo crear el personaje " + nombre)

    sub = unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
    if not sub.try_add_object_to_edit(character=personaje):
        raise RuntimeError("no se pudo abrir %s para editar" % nombre)
    try:
        rig = unreal.MetaHumanCharacterAutoRiggingRequestParams()
        rig.blocking = True
        rig.report_progress = False
        rig.rig_type = unreal.MetaHumanRigType.JOINTS_ONLY
        anotar("%s: pidiendo esqueleto a la nube…" % nombre)
        sub.request_auto_rigging(character=personaje, params=rig)

        tex = unreal.MetaHumanCharacterTextureRequestParams()
        tex.blocking = True
        tex.report_progress = False
        anotar("%s: pidiendo texturas de alta resolución…" % nombre)
        sub.request_texture_sources(character=personaje, params=tex)
        anotar("%s: texturas de alta resolución = %s" % (nombre, personaje.has_high_resolution_textures))

        unreal.EditorAssetLibrary.save_loaded_asset(personaje)

        destino = os.path.join(SALIDA, nombre)
        os.makedirs(destino, exist_ok=True)
        p = unreal.MetaHumanDCCExportParams()
        p.external_path = destino
        p.bake_make_up = False
        p.compress_in_zip_file = False
        anotar("%s: exportando a %s…" % (nombre, destino))
        unreal.MetaHumanCharacterExportBlueprintLibrary.export_dcc(personaje, p)
        archivos = []
        for raiz, _, nombres in os.walk(destino):
            archivos += [os.path.join(raiz, n) for n in nombres]
        anotar("%s: exportados %d archivos" % (nombre, len(archivos)))
    finally:
        if sub.is_object_added_for_editing(character=personaje):
            sub.remove_object_to_edit(character=personaje)


os.makedirs(SALIDA, exist_ok=True)
anotar("=== inicio: %s ===" % ", ".join(NOMBRES))
for n in NOMBRES:
    try:
        crear(n.strip())
    except Exception as e:
        anotar("ERROR en %s: %s" % (n, e))
        anotar(traceback.format_exc())
anotar("=== fin ===")
