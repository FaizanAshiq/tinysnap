#!/usr/bin/env python3
"""fake-portal.py <png>: answers the desktop portal's Screenshot with a copy of <png>, at once,
as GNOME's portal does once the person has allowed screenshots. For CI, where GNOME's own portal
backend cannot run. Owns org.freedesktop.portal.Desktop on the session bus until killed."""
import shutil
import sys
import tempfile

import gi

gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

PICTURE = sys.argv[1]
NODE = Gio.DBusNodeInfo.new_for_xml("""
<node>
  <interface name="org.freedesktop.portal.Screenshot">
    <method name="Screenshot">
      <arg type="s" name="parent_window" direction="in"/>
      <arg type="a{sv}" name="options" direction="in"/>
      <arg type="o" name="handle" direction="out"/>
    </method>
  </interface>
  <interface name="org.freedesktop.portal.Settings">
    <method name="ReadAll">
      <arg type="as" name="namespaces" direction="in"/>
      <arg type="a{sa{sv}}" name="value" direction="out"/>
    </method>
    <method name="Read">
      <arg type="s" name="namespace" direction="in"/>
      <arg type="s" name="key" direction="in"/>
      <arg type="v" name="value" direction="out"/>
    </method>
    <method name="ReadOne">
      <arg type="s" name="namespace" direction="in"/>
      <arg type="s" name="key" direction="in"/>
      <arg type="v" name="value" direction="out"/>
    </method>
    <property name="version" type="u" access="read"/>
  </interface>
</node>""")
# The desktop's settings as GNOME's portal gives them, light style.
SETTINGS = {"org.freedesktop.appearance": {"color-scheme": GLib.Variant("u", 0)}}


def settings(connection, sender, path, interface, method, parameters, invocation):
    if method == "ReadAll":
        (wanted,) = parameters.unpack()
        found = {space: keys for space, keys in SETTINGS.items() if not wanted or space in wanted}
        invocation.return_value(GLib.Variant("(a{sa{sv}})", (found,)))
        return
    space, key = parameters.unpack()
    if key in SETTINGS.get(space, {}):
        value = SETTINGS[space][key]
        invocation.return_value(GLib.Variant("(v)", (GLib.Variant("v", value) if method == "Read" else value,)))
    else:
        invocation.return_dbus_error("org.freedesktop.portal.Error.NotFound", "Requested setting not found")


def version(connection, sender, path, interface, name):
    return GLib.Variant("u", 2)


def called(connection, sender, path, interface, method, parameters, invocation):
    _, options = parameters.unpack()
    handle = "/org/freedesktop/portal/desktop/request/%s/%s" % (sender[1:].replace(".", "_"), options.get("handle_token", "t"))
    invocation.return_value(GLib.Variant("(o)", (handle,)))
    # Tinysnap deletes the file once read, so each answer is a fresh copy.
    copy = tempfile.mktemp(suffix=".png")
    shutil.copy(PICTURE, copy)
    print("screenshot for %s, interactive %s" % (sender, options.get("interactive")), flush=True)
    connection.emit_signal(sender, handle, "org.freedesktop.portal.Request", "Response",
                           GLib.Variant("(ua{sv})", (0, {"uri": GLib.Variant("s", "file://" + copy)})))


def acquired(connection, name):
    screenshot, settings_interface = NODE.interfaces
    connection.register_object("/org/freedesktop/portal/desktop", screenshot, called, None, None)
    connection.register_object("/org/freedesktop/portal/desktop", settings_interface, settings, version, None)


Gio.bus_own_name(Gio.BusType.SESSION, "org.freedesktop.portal.Desktop", Gio.BusNameOwnerFlags.NONE, acquired, None, None)
GLib.MainLoop().run()
