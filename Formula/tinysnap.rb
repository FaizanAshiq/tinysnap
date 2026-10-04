class Tinysnap < Formula
  desc "Menu bar screenshot app: capture, mark up, redact, frame and pin"
  homepage "https://github.com/FaizanAshiq/tinysnap"
  url "https://github.com/FaizanAshiq/tinysnap/archive/refs/tags/v1.4.0.tar.gz"
  sha256 "d6c75dd5b75428a458939c4cbd4e1a29ff2cd60925c00d2d874e43cb7f544d14"
  license "MIT"
  depends_on macos: :sonoma

  def install
    system "./build.sh", "release"
    prefix.install "dist/Tinysnap.app"
    prefix.install "scripts"
  end

  def caveats
    <<~EOS
      Tinysnap was built on this machine, so it launches without a Gatekeeper prompt.

      Open it with:
        open #{prefix}/Tinysnap.app

      Homebrew builds in a sandbox that cannot reach your keychain, so this copy
      is signed ad hoc and macOS forgets Screen Recording on every upgrade. Two
      commands fix that for good:
        #{prefix}/scripts/signing-identity.sh
        codesign --force --sign "Tinysnap Local Signing" #{prefix}/Tinysnap.app

      Screen Recording is the only permission Tinysnap needs, and it asks the
      first time you capture.
    EOS
  end

  test do
    app = prefix/"Tinysnap.app"
    assert_predicate app/"Contents/MacOS/Tinysnap", :executable?
    assert_equal "com.faizanashiq.tinysnap",
                 shell_output("/usr/bin/plutil -extract CFBundleIdentifier raw '#{app}/Contents/Info.plist'").strip
  end
end
