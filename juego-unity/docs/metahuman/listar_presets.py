# Anota el ajuste de piel de cada personaje predefinido de MetaHuman, para elegir por tono.
import os
import unreal

SALIDA = os.path.expanduser("~/penales/ArteFuente/MetaHuman/_presets.txt")
reg = unreal.AssetRegistryHelpers.get_asset_registry()
lineas = []
for d in reg.get_assets_by_path("/MetaHumanCharacter/Optional/Presets", recursive=False):
    nombre = str(d.asset_name)
    try:
        p = d.get_asset()
        s = p.get_editor_property("skin_settings")
        sk = s.get_editor_property("skin")
        fila = "%s u=%.3f v=%.3f cara=%d cuerpo=%d" % (
            nombre, sk.get_editor_property("u"), sk.get_editor_property("v"),
            sk.get_editor_property("face_texture_index"), sk.get_editor_property("body_texture_index"))
        for prop in ("has_high_resolution_textures", "fixed_body_type"):
            try:
                fila += " %s=%s" % (prop, p.get_editor_property(prop))
            except Exception:
                pass
        lineas.append(fila)
    except Exception as e:
        lineas.append("%s ERROR %s" % (nombre, str(e)[:160]))
with open(SALIDA, "w", encoding="utf-8") as f:
    f.write("\n".join(sorted(lineas)) + "\n=== fin ===\n")
unreal.log("[Penales] presets anotados: %d" % len(lineas))
