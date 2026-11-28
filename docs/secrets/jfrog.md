# JFrog (`abox-jfrog`)

The `jfrog` kit sends this value to `rwest.jfrog.io` for the NuGet feed of the squad, in
the install phase and at runtime.

The guided setup asks for two inputs:

- the user name of your JFrog account, shown as you type and offered as the default the
  next time, so rotating the token needs only the token;
- a token of that account, such as an identity token from your JFrog profile.

abox stores `base64("<user name>:<token>")`, the Basic authentication the proxy sends.
