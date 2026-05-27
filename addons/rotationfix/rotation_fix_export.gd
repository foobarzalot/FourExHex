@tool
extends EditorExportPlugin

# Tells the Godot Android gradle build to link the RotationFix AAR (it feeds the
# build template's plugins_local_binaries property). Paths returned by
# _get_android_libraries are relative to res://addons/.


func _get_name() -> String:
	return "RotationFix"


func _supports_platform(platform: EditorExportPlatform) -> bool:
	return platform is EditorExportPlatformAndroid


func _get_android_libraries(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
	var variant := "debug" if debug else "release"
	return PackedStringArray(["rotationfix/bin/%s/RotationFix.aar" % variant])
