@tool
extends EditorPlugin

# Registers the export plugin that injects the MailCompose AAR into Android
# builds. The AAR is built from android_plugin/ (see
# tools/build_android_plugin.sh) and lives in bin/.

const MailComposeExport := preload("res://addons/mailcompose/mail_compose_export.gd")

var _export_plugin: EditorExportPlugin


func _enter_tree() -> void:
	_export_plugin = MailComposeExport.new()
	add_export_plugin(_export_plugin)


func _exit_tree() -> void:
	if _export_plugin:
		remove_export_plugin(_export_plugin)
		_export_plugin = null
