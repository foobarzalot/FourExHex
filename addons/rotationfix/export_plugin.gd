@tool
extends EditorPlugin

# Registers the export plugin that injects the RotationFix AAR into Android
# builds. The AAR itself is built from android_plugin/ (see
# tools/build_android_plugin.sh) and lives in bin/.

const RotationFixExport := preload("res://addons/rotationfix/rotation_fix_export.gd")

var _export_plugin: EditorExportPlugin


func _enter_tree() -> void:
	_export_plugin = RotationFixExport.new()
	add_export_plugin(_export_plugin)


func _exit_tree() -> void:
	if _export_plugin:
		remove_export_plugin(_export_plugin)
		_export_plugin = null
